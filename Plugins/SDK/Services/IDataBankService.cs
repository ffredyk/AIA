using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Service for managing data banks
    /// </summary>
    public interface IDataBankService
    {
        /// <summary>
        /// Gets all categories (read permission required)
        /// </summary>
        IReadOnlyList<IDataBankCategory> GetAllCategories();

        /// <summary>
        /// Gets a category by ID
        /// </summary>
        IDataBankCategory? GetCategoryById(Guid id);

        /// <summary>
        /// Gets all entries in a category
        /// </summary>
        IReadOnlyList<IDataBankEntry> GetEntriesByCategory(Guid categoryId);

        /// <summary>
        /// Gets an entry by ID
        /// </summary>
        IDataBankEntry? GetEntryById(Guid id);

        /// <summary>
        /// Creates a new category (write permission required)
        /// </summary>
        IDataBankCategory CreateCategory(string name, string? color = null);

        /// <summary>
        /// Creates a new entry in a category (write permission required)
        /// </summary>
        IDataBankEntry CreateEntry(Guid categoryId, string title, DataBankEntryType entryType = DataBankEntryType.Text);

        /// <summary>
        /// Deletes a category and all its entries (write permission required)
        /// </summary>
        Task<bool> DeleteCategoryAsync(Guid id);

        /// <summary>
        /// Deletes an entry (write permission required)
        /// </summary>
        Task<bool> DeleteEntryAsync(Guid id);

        /// <summary>
        /// Imports a file as a data bank entry (write permission required)
        /// </summary>
        Task<IDataBankEntry?> ImportFileAsync(Guid categoryId, string filePath);

        /// <summary>
        /// Saves all data banks (write permission required)
        /// </summary>
        Task SaveAsync();

        /// <summary>
        /// Event fired when data banks change
        /// </summary>
        event EventHandler<DataBankChangedEventArgs>? DataBankChanged;
    }

    /// <summary>
    /// Data bank category interface
    /// </summary>
    public interface IDataBankCategory
    {
        Guid Id { get; }
        string Name { get; set; }
        string Color { get; set; }
        int EntryCount { get; }
    }

    /// <summary>
    /// Data bank entry interface
    /// </summary>
    public interface IDataBankEntry
    {
        Guid Id { get; }
        string Title { get; set; }
        string Content { get; set; }
        DataBankEntryType EntryType { get; }
        string? FilePath { get; }
        string? OriginalFileName { get; }
        long FileSize { get; }
        DateTime CreatedDate { get; }
        DateTime ModifiedDate { get; }
        Guid CategoryId { get; }
        string Tags { get; set; }
    }

    public enum DataBankEntryType
    {
        Text,
        TextFile,
        Pdf,
        Email,
        Image,
        Custom
    }

    public class DataBankChangedEventArgs : EventArgs
    {
        public DataBankChangeType ChangeType { get; }
        public object? Item { get; }

        public DataBankChangedEventArgs(DataBankChangeType changeType, object? item = null)
        {
            ChangeType = changeType;
            Item = item;
        }
    }

    public enum DataBankChangeType
    {
        CategoryAdded,
        CategoryUpdated,
        CategoryDeleted,
        EntryAdded,
        EntryUpdated,
        EntryDeleted,
        Reloaded
    }
}
