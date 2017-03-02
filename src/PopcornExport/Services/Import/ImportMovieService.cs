﻿using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using PopcornExport.Helpers;
using PopcornExport.Models.Movie;
using PopcornExport.Services.Database;
using PopcornExport.Services.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Documents.Client;
using Newtonsoft.Json;
using PopcornExport.Services.Assets;
using TMDbLib.Client;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;

namespace PopcornExport.Services.Import
{
    /// <summary>
    /// Import movies
    /// </summary>
    public sealed class ImportMovieService : IImportService
    {
        /// <summary>
        /// The logging service
        /// </summary>
        private readonly ILoggingService _loggingService;

        /// <summary>
        /// DocumentDb service
        /// </summary>
        private readonly IDocumentDbService _documentDbService;

        /// <summary>
        /// Assets service
        /// </summary>
        private readonly IAssetsService _assetsService;

        /// <summary>
        /// Instanciate a <see cref="ImportMovieService"/>
        /// </summary>
        /// <param name="documentDbService">MongoDb service</param>
        /// <param name="assetsService">Assets service</param>
        /// <param name="loggingService">Logging service</param>
        public ImportMovieService(IDocumentDbService documentDbService, IAssetsService assetsService,
            ILoggingService loggingService)
        {
            _documentDbService = documentDbService;
            _loggingService = loggingService;
            _assetsService = assetsService;
        }

        /// <summary>
        /// Import movies to database
        /// </summary>
        /// <param name="docs">Documents to import</param>
        /// <returns><see cref="Task"/></returns>
        public async Task Import(IEnumerable<BsonDocument> docs)
        {
            var documents = docs.ToList();
            var loggingTraceBegin =
                $@"Import {documents.Count} movies started at {DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss.fff",
                    CultureInfo.InvariantCulture)}";
            _loggingService.Telemetry.TrackTrace(loggingTraceBegin);

            var updatedMovies = 0;
            var tmdbClient = new TMDbClient(Constants.TmdbClientApiKey);
            tmdbClient.GetConfig();
            using (var client = _documentDbService.Client)
            {
                foreach (var document in documents)
                {
                    try
                    {
                        var watch = new Stopwatch();
                        watch.Start();

                        // Deserialize a document to a movie
                        var movie =
                            JsonConvert.DeserializeObject<MovieJson>(
                                BsonSerializer.Deserialize<MovieBson>(document).ToJson());

                        await RetrieveAssets(tmdbClient, movie);

                        await client.UpsertDocumentAsync(
                            UriFactory.CreateDocumentCollectionUri(Constants.DatabaseName,
                                Constants.MoviesCollectionName),
                            movie);

                        watch.Stop();
                        updatedMovies++;
                        Console.WriteLine(Environment.NewLine);
                        Console.WriteLine(
                            $"{DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture)} UPDATED MOVIE {movie.Title} in {watch.ElapsedMilliseconds} ms. {updatedMovies}/{documents.Count}");
                    }
                    catch (Exception ex)
                    {
                        _loggingService.Telemetry.TrackException(ex);
                    }
                }
            }

            // Finish
            Console.WriteLine(Environment.NewLine);
            Console.WriteLine("Done processing movies.");

            var loggingTraceEnd =
                $@"Import movies ended at {DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss.fff",
                    CultureInfo.InvariantCulture)}";
            _loggingService.Telemetry.TrackTrace(loggingTraceEnd);
        }

        /// <summary>
        /// Retrieve assets for the provided movie
        /// </summary>
        /// <param name="tmdbClient"><see cref="TMDbClient"/></param>
        /// <param name="movie">Movie to update</param>
        /// <returns></returns>
        private async Task RetrieveAssets(TMDbClient tmdbClient, MovieJson movie)
        {
            var tmdbMovie = await tmdbClient.GetMovieAsync(movie.ImdbCode, MovieMethods.Images);

            var tasks = new List<Task>
            {
                Task.Run(async () =>
                {
                    if (tmdbMovie.Images?.Backdrops != null && tmdbMovie.Images.Backdrops.Any())
                    {
                        var backdrop = GetImagePathFromTmdb(tmdbClient,
                            tmdbMovie.Images.Backdrops.Aggregate(
                                (image1, image2) =>
                                    image1 != null && image2 != null && image1.Width < image2.Width
                                        ? image2
                                        : image1));
                        movie.BackdropImage =
                            await _assetsService.UploadFile(
                                $@"images/{movie.ImdbCode}/backdrop/{backdrop.Split('/').Last()}",
                                backdrop);
                    }
                }),
                Task.Run(async () =>
                {
                    if (tmdbMovie.Images?.Posters != null && tmdbMovie.Images.Posters.Any())
                    {
                        var poster = GetImagePathFromTmdb(tmdbClient,
                            tmdbMovie.Images.Posters.Aggregate(
                                (image1, image2) =>
                                    image1 != null && image2 != null && image1.VoteAverage < image2.VoteAverage
                                        ? image2
                                        : image1));
                        movie.PosterImage =
                            await _assetsService.UploadFile(
                                $@"images/{movie.ImdbCode}/poster/{poster.Split('/').Last()}",
                                poster);
                    }
                }),
                Task.Run(async () =>
                {
                    if (!string.IsNullOrWhiteSpace(movie.BackgroundImage))
                    {
                        movie.BackgroundImage =
                            await _assetsService.UploadFile(
                                $@"images/{movie.ImdbCode}/background/{movie.BackgroundImage.Split('/').Last()}",
                                movie.BackgroundImage);
                    }
                }),
                Task.Run(async () =>
                {
                    if (!string.IsNullOrWhiteSpace(movie.SmallCoverImage))
                    {
                        movie.SmallCoverImage =
                            await _assetsService.UploadFile(
                                $@"images/{movie.ImdbCode}/cover/small/{movie.SmallCoverImage.Split('/').Last()}",
                                movie.SmallCoverImage);
                    }
                }),
                Task.Run(async () =>
                {
                    if (!string.IsNullOrWhiteSpace(movie.MediumCoverImage))
                    {
                        movie.MediumCoverImage =
                            await _assetsService.UploadFile(
                                $@"images/{movie.ImdbCode}/cover/medium/{movie.MediumCoverImage.Split('/')
                                    .Last()}",
                                movie.MediumCoverImage);
                    }
                }),
                Task.Run(async () =>
                {
                    if (!string.IsNullOrWhiteSpace(movie.LargeCoverImage))
                    {
                        movie.LargeCoverImage =
                            await _assetsService.UploadFile(
                                $@"images/{movie.ImdbCode}/cover/large/{movie.LargeCoverImage.Split('/').Last()}",
                                movie.LargeCoverImage);
                    }
                }),
                Task.Run(async () =>
                {
                    if (!string.IsNullOrWhiteSpace(movie.MediumScreenshotImage1))
                    {
                        movie.MediumScreenshotImage1 =
                            await _assetsService.UploadFile(
                                $@"images/{movie.ImdbCode}/screenshot/medium/1/{movie.MediumScreenshotImage1
                                    .Split('/')
                                    .Last()}", movie.MediumScreenshotImage1);
                    }
                }),
                Task.Run(async () =>
                {
                    if (!string.IsNullOrWhiteSpace(movie.MediumScreenshotImage2))
                    {
                        movie.MediumScreenshotImage2 =
                            await _assetsService.UploadFile(
                                $@"images/{movie.ImdbCode}/screenshot/medium/2/{movie.MediumScreenshotImage2
                                    .Split('/')
                                    .Last()}", movie.MediumScreenshotImage2);
                    }
                }),
                Task.Run(async () =>
                {
                    if (!string.IsNullOrWhiteSpace(movie.MediumScreenshotImage3))
                    {
                        movie.MediumScreenshotImage3 =
                            await _assetsService.UploadFile(
                                $@"images/{movie.ImdbCode}/screenshot/medium/3/{movie.MediumScreenshotImage3
                                    .Split('/')
                                    .Last()}", movie.MediumScreenshotImage3);
                    }
                }),
                Task.Run(async () =>
                {
                    if (!string.IsNullOrWhiteSpace(movie.LargeScreenshotImage1))
                    {
                        movie.LargeScreenshotImage1 =
                            await _assetsService.UploadFile(
                                $@"images/{movie.ImdbCode}/screenshot/large/1/{movie.LargeScreenshotImage1.Split
                                    ('/')
                                    .Last()}", movie.LargeScreenshotImage1);
                    }
                }),
                Task.Run(async () =>
                {
                    if (!string.IsNullOrWhiteSpace(movie.LargeScreenshotImage2))
                    {
                        movie.LargeScreenshotImage2 =
                            await _assetsService.UploadFile(
                                $@"images/{movie.ImdbCode}/screenshot/large/2/{movie.LargeScreenshotImage2.Split
                                    ('/')
                                    .Last()}", movie.LargeScreenshotImage2);
                    }
                }),
                Task.Run(async () =>
                {
                    if (!string.IsNullOrWhiteSpace(movie.LargeScreenshotImage3))
                    {
                        movie.LargeScreenshotImage3 =
                            await _assetsService.UploadFile(
                                $@"images/{movie.ImdbCode}/screenshot/large/3/{movie.LargeScreenshotImage3.Split
                                    ('/')
                                    .Last()}", movie.LargeScreenshotImage3);
                    }
                }),
                Task.Run(async () =>
                {
                    if (movie.Torrents != null)
                    {
                        foreach (var torrent in movie.Torrents)
                        {
                            torrent.Url =
                                await _assetsService.UploadFile(
                                    $@"torrents/{movie.ImdbCode}/{torrent.Quality}/{movie.ImdbCode}.torrent",
                                    torrent.Url);
                        }
                    }
                }),
                Task.Run(async () =>
                {
                    if (movie.Cast != null)
                    {
                        foreach (var cast in movie.Cast)
                        {
                            if (!string.IsNullOrWhiteSpace(cast.SmallImage))
                            {
                                cast.SmallImage = await _assetsService.UploadFile(
                                    $@"images/{movie.ImdbCode}/cast/{cast.ImdbCode}/{cast.SmallImage.Split
                                        ('/')
                                        .Last()}", cast.SmallImage);
                            }
                        }
                    }
                })
            };

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Retrieve an image from Tmdb
        /// </summary>
        /// <param name="client"><see cref="TMDbClient"/></param>
        /// <param name="image">Image to retrieve</param>
        /// <returns></returns>
        private string GetImagePathFromTmdb(TMDbClient client, ImageData image)
        {
            return client.GetImageUrl("original", image.FilePath).AbsoluteUri;
        }
    }
}