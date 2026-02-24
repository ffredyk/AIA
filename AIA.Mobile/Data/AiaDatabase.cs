using SQLite;
using AIA.Domain.Models;

namespace AIA.Mobile.Data
{
    /// <summary>
    /// SQLite database context for AIA mobile app
    /// </summary>
    public class AiaDatabase
    {
        private readonly SQLiteAsyncConnection _database;

        public AiaDatabase(string dbPath)
        {
            _database = new SQLiteAsyncConnection(dbPath);
            
            // Create tables
            _database.CreateTableAsync<TaskItem>().Wait();
            _database.CreateTableAsync<ReminderItem>().Wait();
            _database.CreateTableAsync<DataBankCategory>().Wait();
            _database.CreateTableAsync<DataBankEntry>().Wait();
            _database.CreateTableAsync<ChatSession>().Wait();
            _database.CreateTableAsync<ChatMessage>().Wait();
            _database.CreateTableAsync<AIProvider>().Wait();
            _database.CreateTableAsync<SyncChange>().Wait();
            _database.CreateTableAsync<SyncConflict>().Wait();
        }

        #region Tasks

        public Task<List<TaskItem>> GetTasksAsync()
        {
            return _database.Table<TaskItem>()
                .Where(t => !t.IsArchived)
                .OrderByDescending(t => t.CreatedDate)
                .ToListAsync();
        }

        public Task<TaskItem> GetTaskAsync(Guid id)
        {
            return _database.Table<TaskItem>()
                .Where(t => t.Id == id)
                .FirstOrDefaultAsync();
        }

        public async Task<int> SaveTaskAsync(TaskItem task)
        {
            task.ModifiedDate = DateTime.Now;
            task.IsSyncPending = true;
            
            if (await GetTaskAsync(task.Id) != null)
            {
                return await _database.UpdateAsync(task);
            }
            else
            {
                return await _database.InsertAsync(task);
            }
        }

        public Task<int> DeleteTaskAsync(TaskItem task)
        {
            task.IsSyncPending = true;
            return _database.DeleteAsync(task);
        }

        #endregion

        #region Reminders

        public Task<List<ReminderItem>> GetRemindersAsync()
        {
            return _database.Table<ReminderItem>()
                .OrderBy(r => r.DueDate)
                .ToListAsync();
        }

        public Task<ReminderItem> GetReminderAsync(Guid id)
        {
            return _database.Table<ReminderItem>()
                .Where(r => r.Id == id)
                .FirstOrDefaultAsync();
        }

        public async Task<int> SaveReminderAsync(ReminderItem reminder)
        {
            reminder.ModifiedDate = DateTime.Now;
            reminder.IsSyncPending = true;
            
            if (await GetReminderAsync(reminder.Id) != null)
            {
                return await _database.UpdateAsync(reminder);
            }
            else
            {
                return await _database.InsertAsync(reminder);
            }
        }

        public Task<int> DeleteReminderAsync(ReminderItem reminder)
        {
            reminder.IsSyncPending = true;
            return _database.DeleteAsync(reminder);
        }

        #endregion

        #region Data Banks

        public Task<List<DataBankCategory>> GetCategoriesAsync()
        {
            return _database.Table<DataBankCategory>()
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public Task<DataBankCategory> GetCategoryAsync(Guid id)
        {
            return _database.Table<DataBankCategory>()
                .Where(c => c.Id == id)
                .FirstOrDefaultAsync();
        }

        public async Task<int> SaveCategoryAsync(DataBankCategory category)
        {
            category.ModifiedDate = DateTime.Now;
            category.IsSyncPending = true;
            
            if (await GetCategoryAsync(category.Id) != null)
            {
                return await _database.UpdateAsync(category);
            }
            else
            {
                return await _database.InsertAsync(category);
            }
        }

        public Task<int> DeleteCategoryAsync(DataBankCategory category)
        {
            category.IsSyncPending = true;
            return _database.DeleteAsync(category);
        }

        public Task<List<DataBankEntry>> GetEntriesAsync(Guid categoryId)
        {
            return _database.Table<DataBankEntry>()
                .Where(e => e.CategoryId == categoryId)
                .OrderByDescending(e => e.CreatedDate)
                .ToListAsync();
        }

        public Task<DataBankEntry> GetEntryAsync(Guid id)
        {
            return _database.Table<DataBankEntry>()
                .Where(e => e.Id == id)
                .FirstOrDefaultAsync();
        }

        public async Task<int> SaveEntryAsync(DataBankEntry entry)
        {
            entry.ModifiedDate = DateTime.Now;
            entry.IsSyncPending = true;
            
            if (await GetEntryAsync(entry.Id) != null)
            {
                return await _database.UpdateAsync(entry);
            }
            else
            {
                return await _database.InsertAsync(entry);
            }
        }

        public Task<int> DeleteEntryAsync(DataBankEntry entry)
        {
            entry.IsSyncPending = true;
            return _database.DeleteAsync(entry);
        }

        #endregion

        #region Chat

        public Task<List<ChatSession>> GetChatSessionsAsync()
        {
            return _database.Table<ChatSession>()
                .OrderByDescending(c => c.ModifiedDate)
                .ToListAsync();
        }

        public Task<ChatSession> GetChatSessionAsync(Guid id)
        {
            return _database.Table<ChatSession>()
                .Where(c => c.Id == id)
                .FirstOrDefaultAsync();
        }

        public async Task<int> SaveChatSessionAsync(ChatSession session)
        {
            session.ModifiedDate = DateTime.Now;
            session.IsSyncPending = true;
            
            if (await GetChatSessionAsync(session.Id) != null)
            {
                return await _database.UpdateAsync(session);
            }
            else
            {
                return await _database.InsertAsync(session);
            }
        }

        public Task<int> DeleteChatSessionAsync(ChatSession session)
        {
            session.IsSyncPending = true;
            return _database.DeleteAsync(session);
        }

        public Task<List<ChatMessage>> GetChatMessagesAsync(Guid sessionId)
        {
            return _database.Table<ChatMessage>()
                .OrderBy(m => m.Timestamp)
                .ToListAsync();
        }

        public async Task<int> SaveChatMessageAsync(ChatMessage message)
        {
            return await _database.InsertAsync(message);
        }

        #endregion

        #region AI Providers

        public Task<List<AIProvider>> GetAIProvidersAsync()
        {
            return _database.Table<AIProvider>()
                .Where(p => p.IsEnabled)
                .ToListAsync();
        }

        public Task<AIProvider> GetDefaultAIProviderAsync()
        {
            return _database.Table<AIProvider>()
                .Where(p => p.IsDefault && p.IsEnabled)
                .FirstOrDefaultAsync();
        }

        public async Task<int> SaveAIProviderAsync(AIProvider provider)
        {
            provider.IsSyncPending = true;
            
            var existing = await _database.Table<AIProvider>()
                .Where(p => p.Id == provider.Id)
                .FirstOrDefaultAsync();
            
            if (existing != null)
            {
                return await _database.UpdateAsync(provider);
            }
            else
            {
                return await _database.InsertAsync(provider);
            }
        }

        public Task<int> DeleteAIProviderAsync(AIProvider provider)
        {
            return _database.DeleteAsync(provider);
        }

        #endregion

        #region Sync

        public Task<List<SyncChange>> GetPendingSyncChangesAsync()
        {
            return _database.Table<SyncChange>()
                .Where(c => !c.IsSynced)
                .OrderBy(c => c.Timestamp)
                .ToListAsync();
        }

        public Task<int> SaveSyncChangeAsync(SyncChange change)
        {
            return _database.InsertAsync(change);
        }

        public Task<int> MarkSyncChangeAsSyncedAsync(SyncChange change)
        {
            change.IsSynced = true;
            return _database.UpdateAsync(change);
        }

        public Task<List<SyncConflict>> GetUnresolvedConflictsAsync()
        {
            return _database.Table<SyncConflict>()
                .Where(c => c.Resolution == null)
                .ToListAsync();
        }

        public Task<int> SaveSyncConflictAsync(SyncConflict conflict)
        {
            return _database.InsertAsync(conflict);
        }

        public Task<int> ResolveSyncConflictAsync(SyncConflict conflict)
        {
            return _database.UpdateAsync(conflict);
        }

        #endregion
    }
}
