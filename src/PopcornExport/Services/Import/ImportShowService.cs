﻿using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using PopcornExport.Helpers;
using PopcornExport.Services.Database;
using PopcornExport.Services.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using PopcornExport.Models.Show;
using PopcornExport.Services.Assets;

namespace PopcornExport.Services.Import
{
    /// <summary>
    /// Import shows
    /// </summary>
    public sealed class ImportShowService : IImportService
    {
        /// <summary>
        /// The logging service
        /// </summary>
        private readonly ILoggingService _loggingService;

        /// <summary>
        /// MongoDb service
        /// </summary>
        private readonly IMongoDbService<BsonDocument> _mongoDbService;

        /// <summary>
        /// Assets service
        /// </summary>
        private readonly IAssetsService _assetsService;

        /// <summary>
        /// Instanciate a <see cref="ImportShowService"/>
        /// </summary>
        /// <param name="mongoDbService">MongoDb service</param>
        /// <param name="assetsService">Assets service</param>
        /// <param name="loggingService">Logging service</param>
        public ImportShowService(IMongoDbService<BsonDocument> mongoDbService, IAssetsService assetsService, ILoggingService loggingService)
        {
            _mongoDbService = mongoDbService;
            _assetsService = assetsService;
            _loggingService = loggingService;
        }

        /// <summary>
        /// Import shows to database
        /// </summary>
        /// <param name="docs">Documents to import</param>
        /// <returns><see cref="Task"/></returns>
        public async Task Import(IEnumerable<BsonDocument> docs)
        {
            var documents = docs.ToList();
            var loggingTraceBegin =
                $@"Import {documents.Count} shows started at {DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss.fff",
                    CultureInfo.InvariantCulture)}";
            _loggingService.Telemetry.TrackTrace(loggingTraceBegin);

            var watch = new Stopwatch();
            var updatedshows = 0;

            foreach (var document in documents)
            {
                try
                {
                    // Deserialize a document to a show
                    var show = BsonSerializer.Deserialize<ShowBson>(document);

                    if(!string.IsNullOrEmpty(show.Images.Banner))
                        show.Images.Banner = await _assetsService.UploadFile($@"images/{show.ImdbId}/banner/{show.Images.Banner.Split('/').Last()}", show.Images.Banner);

                    if (!string.IsNullOrEmpty(show.Images.Fanart))
                        show.Images.Fanart = await _assetsService.UploadFile($@"images/{show.ImdbId}/fanart/{show.Images.Fanart.Split('/').Last()}", show.Images.Fanart);

                    if (!string.IsNullOrEmpty(show.Images.Poster))
                        show.Images.Poster = await _assetsService.UploadFile($@"images/{show.ImdbId}/poster/{show.Images.Poster.Split('/').Last()}", show.Images.Poster);

                    // Set filter to search a show in database
                    var filter = Builders<BsonDocument>.Filter.Eq("imdb_id", show.ImdbId);

                    // Set udpate builder to update a show
                    var update = Builders<BsonDocument>.Update.Set("imdb_id", show.ImdbId)
                        .Set("tvdb_id", show.TvdbId)
                        .Set("title", show.Title)
                        .Set("year", show.Year)
                        .Set("slug", show.Slug)
                        .Set("synopsis", show.Synopsis)
                        .Set("runtime", show.Runtime)
                        .Set("country", show.Country)
                        .Set("network", show.Network)
                        .Set("air_day", show.AirDay)
                        .Set("air_time", show.AirTime)
                        .Set("status", show.Status)
                        .Set("num_seasons", show.NumSeasons)
                        .Set("last_updated", show.LastUpdated)
                        .Set("__v", show.V)
                        .Set("episodes", show.Episodes)
                        .Set("genres", show.Genres)
                        .Set("images", show.Images)
                        .Set("rating", show.Rating);

                    // If a show does not exist in database, create it
                    var upsert = new FindOneAndUpdateOptions<BsonDocument>()
                    {
                        IsUpsert = true
                    };

                    // Retrieve shows from database
                    var collectionShows = _mongoDbService.GetCollection(Constants.ShowsCollectionName);

                    watch.Restart();

                    // Update show
                    await collectionShows.FindOneAndUpdateAsync(filter, update, upsert);
                    watch.Stop();
                    updatedshows++;
                    Console.WriteLine(Environment.NewLine);
                    Console.Write($"{DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture)}");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("  UPDATED SHOW ");

                    // Sum up
                    Console.ResetColor();
                    Console.Write($"{show.Title} in {watch.ElapsedMilliseconds} ms.");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"  {updatedshows}/{documents.Count}");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    _loggingService.Telemetry.TrackException(ex);
                }
            }

            // Finish
            Console.WriteLine(Environment.NewLine);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Done processing shows.");
            Console.ResetColor();

            var loggingTraceEnd =
                $@"Import shows ended at {DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss.fff",
                    CultureInfo.InvariantCulture)}";
            _loggingService.Telemetry.TrackTrace(loggingTraceEnd);
        }
    }
}