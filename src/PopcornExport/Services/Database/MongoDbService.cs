﻿using MongoDB.Bson;
using MongoDB.Driver;
using PopcornExport.Helpers;
using System.Security.Authentication;

namespace PopcornExport.Services.Database
{
    /// <summary>
    /// MongoDb service
    /// </summary>
    /// <typeparam name="T">Type of the data in collections</typeparam>
    public sealed class MongoDbService<T> : IMongoDbService<T> where T : BsonDocument
    {
        /// <summary>
        /// Mongo database
        /// </summary>
        private readonly IMongoDatabase _db;

        /// <summary>
        /// Establish a connection to database
        /// </summary>
        /// <param name="connectionString">MongoDb connection string</param>
        public MongoDbService(string connectionString)
        {
            var settings = MongoClientSettings.FromUrl(
                new MongoUrl(connectionString)
            );

            settings.SslSettings = new SslSettings {EnabledSslProtocols = SslProtocols.Tls12};
            IMongoClient client = new MongoClient(settings);
            _db = client.GetDatabase(Constants.MongoDbName);
        }

        /// <summary>
        /// Retrieve a collection
        /// </summary>
        /// <param name="collectionName">Collection name to retrieve</param>
        /// <returns><see cref="IMongoCollection{TDocument}"/></returns>
        public IMongoCollection<T> GetCollection(string collectionName)
        {
            return _db.GetCollection<T>(collectionName);
        }
    }
}