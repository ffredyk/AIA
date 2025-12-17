using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIA.Models;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Host.Services
{
    /// <summary>
    /// Host implementation of data bank service that bridges to OverlayViewModel
    /// </summary>
    public class HostDataBankService : IDataBankService
    {
        private readonly Func<OverlayViewModel> _viewModelProvider;

        public event EventHandler<DataBankChangedEventArgs>? DataBankChanged;

        public HostDataBankService(Func<OverlayViewModel> viewModelProvider)
        {
            _viewModelProvider = viewModelProvider ?? throw new ArgumentNullException(nameof(viewModelProvider));
        }

        private OverlayViewModel ViewModel => _viewModelProvider();

        public IReadOnlyList<IDataBankCategory> GetAllCategories()
        {
            return ViewModel.DataBankCategories.Select(c => new DataBankCategoryAdapter(c)).ToList();
        }

        public IDataBankCategory? GetCategoryById(Guid id)
        {
            var category = ViewModel.DataBankCategories.FirstOrDefault(c => c.Id == id);
            return category != null ? new DataBankCategoryAdapter(category) : null;
        }

        public IReadOnlyList<IDataBankEntry> GetEntriesByCategory(Guid categoryId)
        {
            // Access the private _allEntries through a new method or reflection
            // For now, we'll use the CurrentCategoryEntries if category matches
            if (ViewModel.SelectedCategory?.Id == categoryId)
            {
                return ViewModel.CurrentCategoryEntries.Select(e => new DataBankEntryAdapter(e)).ToList();
            }

            // Try to get entries by temporarily selecting the category
            var originalCategory = ViewModel.SelectedCategory;
            var category = ViewModel.DataBankCategories.FirstOrDefault(c => c.Id == categoryId);
            if (category == null) return new List<IDataBankEntry>();

            ViewModel.SelectedCategory = category;
            var entries = ViewModel.CurrentCategoryEntries.Select(e => new DataBankEntryAdapter(e)).ToList();
            ViewModel.SelectedCategory = originalCategory;

            return entries;
        }

        public IDataBankEntry? GetEntryById(Guid id)
        {
            // Search through all categories
            foreach (var category in ViewModel.DataBankCategories)
            {
                var originalCategory = ViewModel.SelectedCategory;
                ViewModel.SelectedCategory = category;
                var entry = ViewModel.CurrentCategoryEntries.FirstOrDefault(e => e.Id == id);
                ViewModel.SelectedCategory = originalCategory;

                if (entry != null)
                    return new DataBankEntryAdapter(entry);
            }
            return null;
        }

        public IDataBankCategory CreateCategory(string name, string? color = null)
        {
            var category = new DataBankCategory
            {
                Name = name,
                Color = color ?? "#0078D4"
            };

            ViewModel.DataBankCategories.Add(category);
            DataBankChanged?.Invoke(this, new DataBankChangedEventArgs(DataBankChangeType.CategoryAdded, new DataBankCategoryAdapter(category)));

            return new DataBankCategoryAdapter(category);
        }

        public IDataBankEntry CreateEntry(Guid categoryId, string title, SDK.DataBankEntryType entryType = SDK.DataBankEntryType.Text)
        {
            var category = ViewModel.DataBankCategories.FirstOrDefault(c => c.Id == categoryId);
            if (category == null)
                throw new ArgumentException($"Category with ID {categoryId} not found", nameof(categoryId));

            var entry = new DataBankEntry
            {
                Title = title,
                EntryType = (Models.DataEntryType)(int)entryType,
                CategoryId = categoryId
            };

            ViewModel.SelectedCategory = category;
            ViewModel.CurrentCategoryEntries.Add(entry);
            category.EntryCount++;

            DataBankChanged?.Invoke(this, new DataBankChangedEventArgs(DataBankChangeType.EntryAdded, new DataBankEntryAdapter(entry)));

            return new DataBankEntryAdapter(entry);
        }

        public async Task<bool> DeleteCategoryAsync(Guid id)
        {
            var category = ViewModel.DataBankCategories.FirstOrDefault(c => c.Id == id);
            if (category == null) return false;

            await ViewModel.DeleteCategoryAsync(category);
            DataBankChanged?.Invoke(this, new DataBankChangedEventArgs(DataBankChangeType.CategoryDeleted));
            return true;
        }

        public async Task<bool> DeleteEntryAsync(Guid id)
        {
            var entry = GetEntryById(id);
            if (entry == null) return false;

            // Find the actual entry object
            foreach (var category in ViewModel.DataBankCategories)
            {
                var originalCategory = ViewModel.SelectedCategory;
                ViewModel.SelectedCategory = category;
                var actualEntry = ViewModel.CurrentCategoryEntries.FirstOrDefault(e => e.Id == id);
                if (actualEntry != null)
                {
                    await ViewModel.DeleteEntryAsync(actualEntry);
                    ViewModel.SelectedCategory = originalCategory;
                    DataBankChanged?.Invoke(this, new DataBankChangedEventArgs(DataBankChangeType.EntryDeleted));
                    return true;
                }
                ViewModel.SelectedCategory = originalCategory;
            }

            return false;
        }

        public async Task<IDataBankEntry?> ImportFileAsync(Guid categoryId, string filePath)
        {
            var category = ViewModel.DataBankCategories.FirstOrDefault(c => c.Id == categoryId);
            if (category == null) return null;

            ViewModel.SelectedCategory = category;
            await ViewModel.ImportFileAsync(filePath);

            var entry = ViewModel.CurrentCategoryEntries.LastOrDefault();
            if (entry != null)
            {
                DataBankChanged?.Invoke(this, new DataBankChangedEventArgs(DataBankChangeType.EntryAdded, new DataBankEntryAdapter(entry)));
                return new DataBankEntryAdapter(entry);
            }

            return null;
        }

        public async Task SaveAsync()
        {
            await ViewModel.SaveDataBanksAsync();
        }
    }

    internal class DataBankCategoryAdapter : IDataBankCategory
    {
        private readonly DataBankCategory _category;

        public DataBankCategoryAdapter(DataBankCategory category)
        {
            _category = category ?? throw new ArgumentNullException(nameof(category));
        }

        public Guid Id => _category.Id;

        public string Name
        {
            get => _category.Name;
            set => _category.Name = value;
        }

        public string Color
        {
            get => _category.Color;
            set => _category.Color = value;
        }

        public int EntryCount => _category.EntryCount;
    }

    internal class DataBankEntryAdapter : IDataBankEntry
    {
        private readonly DataBankEntry _entry;

        public DataBankEntryAdapter(DataBankEntry entry)
        {
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        }

        public Guid Id => _entry.Id;

        public string Title
        {
            get => _entry.Title;
            set => _entry.Title = value;
        }

        public string Content
        {
            get => _entry.Content;
            set => _entry.Content = value;
        }

        public SDK.DataBankEntryType EntryType => (SDK.DataBankEntryType)(int)_entry.EntryType;
        public string? FilePath => _entry.FilePath;
        public string? OriginalFileName => _entry.OriginalFileName;
        public long FileSize => _entry.FileSize;
        public DateTime CreatedDate => _entry.CreatedDate;
        public DateTime ModifiedDate => _entry.ModifiedDate;
        public Guid CategoryId => _entry.CategoryId;

        public string Tags
        {
            get => _entry.Tags;
            set => _entry.Tags = value;
        }
    }
}
