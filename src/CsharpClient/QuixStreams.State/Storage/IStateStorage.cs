﻿using System.Threading.Tasks;

namespace QuixStreams.State.Storage
{
    /// <summary>
    /// The minimum definition for a state storage
    /// </summary>
    public interface IStateStorage
    {
        /// <summary>
        /// Save raw data into the key
        /// </summary>
        /// <param name="key">Key of the element</param>
        /// <param name="data">Raw byte[] representation of data</param>
        /// <returns>Awaitable task</returns>
        public Task SaveRaw(string key, byte[] data);

        /// <summary>
        /// Load raw data from the key
        /// </summary>
        /// <param name="key">Key of the element</param>
        /// <returns>Awaitable result for raw byte[] representation of data</returns>
        public Task<byte[]> LoadRaw(string key);

        /// <summary>
        /// Remove key from the storage
        /// </summary>
        /// <param name="key">Key of the element</param>
        /// <returns>Awaitable task</returns>
        public Task RemoveAsync(string key);

        /// <summary>
        /// Check if storage contains key
        /// </summary>
        /// <param name="key">Key of the element</param>
        /// <returns>Awaitable result for boolean representing whether the storage contains key</returns>
        public Task<bool> ContainsKeyAsync(string key);

        /// <summary>
        /// Get list of all keys in the storage
        /// This function is written in the asynchronous manner
        /// </summary>
        /// <returns>Awaitable result for the keys as a hash set</returns>
        public Task<string[]> GetAllKeysAsync();

        /// <summary>
        /// Clear the storage / remove all keys from the storage
        /// This function is written in the asynchronous manner and returns Task
        /// </summary>
        /// <returns>Awaitable task</returns>
        public Task ClearAsync();
    }
}