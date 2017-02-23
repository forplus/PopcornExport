﻿using MongoDB.Bson;
using MongoDB.Driver;
namespace PopcornExport.Services.Database
{
    /// <summary>
    /// Database service
    /// </summary>
    /// <typeparam name="T">Type of the data in collections</typeparam>
    public interface IMongoDbService<T> where T : BsonDocument
    {
        /// <summary>
        /// Retrieve a collection
        /// </summary>
        /// <param name="collectionName">Collection name to retrieve</param>
        /// <returns></returns>
        IMongoCollection<T> GetCollection(string collectionName);
    }
}
