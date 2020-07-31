﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Helpers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers
{
    /// <summary>
    /// The hls segment controller.
    /// </summary>
    public class HlsSegmentController : BaseJellyfinApiController
    {
        private readonly IFileSystem _fileSystem;
        private readonly IServerConfigurationManager _serverConfigurationManager;
        private readonly TranscodingJobHelper _transcodingJobHelper;

        /// <summary>
        /// Initializes a new instance of the <see cref="HlsSegmentController"/> class.
        /// </summary>
        /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
        /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
        /// <param name="transcodingJobHelper">Initialized instance of the <see cref="TranscodingJobHelper"/>.</param>
        public HlsSegmentController(
            IFileSystem fileSystem,
            IServerConfigurationManager serverConfigurationManager,
            TranscodingJobHelper transcodingJobHelper)
        {
            _fileSystem = fileSystem;
            _serverConfigurationManager = serverConfigurationManager;
            _transcodingJobHelper = transcodingJobHelper;
        }

        /// <summary>
        /// Gets the specified audio segment for an audio item.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="segmentId">The segment id.</param>
        /// <response code="200">Hls audio segment returned.</response>
        /// <returns>A <see cref="FileStreamResult"/> containing the audio stream.</returns>
        // Can't require authentication just yet due to seeing some requests come from Chrome without full query string
        // [Authenticated]
        [HttpGet("/Audio/{itemId}/hls/{segmentId}/stream.mp3")]
        [HttpGet("/Audio/{itemId}/hls/{segmentId}/stream.aac")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "itemId", Justification = "Required for ServiceStack")]
        public ActionResult GetHlsAudioSegmentLegacy([FromRoute] string itemId, [FromRoute] string segmentId)
        {
            // TODO: Deprecate with new iOS app
            var file = segmentId + Path.GetExtension(Request.Path);
            file = Path.Combine(_serverConfigurationManager.GetTranscodePath(), file);

            return FileStreamResponseHelpers.GetStaticFileResult(file, MimeTypes.GetMimeType(file)!, false, this);
        }

        /// <summary>
        /// Gets a hls video playlist.
        /// </summary>
        /// <param name="itemId">The video id.</param>
        /// <param name="playlistId">The playlist id.</param>
        /// <response code="200">Hls video playlist returned.</response>
        /// <returns>A <see cref="FileStreamResult"/> containing the playlist.</returns>
        [HttpGet("/Videos/{itemId}/hls/{playlistId}/stream.m3u8")]
        [Authorize(Policy = Policies.DefaultAuthorization)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "itemId", Justification = "Required for ServiceStack")]
        public ActionResult GetHlsPlaylistLegacy([FromRoute] string itemId, [FromRoute] string playlistId)
        {
            var file = playlistId + Path.GetExtension(Request.Path);
            file = Path.Combine(_serverConfigurationManager.GetTranscodePath(), file);

            return GetFileResult(file, file);
        }

        /// <summary>
        /// Stops an active encoding.
        /// </summary>
        /// <param name="deviceId">The device id of the client requesting. Used to stop encoding processes when needed.</param>
        /// <param name="playSessionId">The play session id.</param>
        /// <response code="204">Encoding stopped successfully.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpDelete("/Videos/ActiveEncodings")]
        [Authorize(Policy = Policies.DefaultAuthorization)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult StopEncodingProcess([FromQuery] string deviceId, [FromQuery] string playSessionId)
        {
            _transcodingJobHelper.KillTranscodingJobs(deviceId, playSessionId, path => true);
            return NoContent();
        }

        /// <summary>
        /// Gets a hls video segment.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="playlistId">The playlist id.</param>
        /// <param name="segmentId">The segment id.</param>
        /// <param name="segmentContainer">The segment container.</param>
        /// <response code="200">Hls video segment returned.</response>
        /// <returns>A <see cref="FileStreamResult"/> containing the video segment.</returns>
        // Can't require authentication just yet due to seeing some requests come from Chrome without full query string
        // [Authenticated]
        [HttpGet("/Videos/{itemId}/hls/{playlistId}/{segmentId}.{segmentContainer}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "itemId", Justification = "Required for ServiceStack")]
        public ActionResult GetHlsVideoSegmentLegacy(
            [FromRoute] string itemId,
            [FromRoute] string playlistId,
            [FromRoute] string segmentId,
            [FromRoute] string segmentContainer)
        {
            var file = segmentId + Path.GetExtension(Request.Path);
            var transcodeFolderPath = _serverConfigurationManager.GetTranscodePath();

            file = Path.Combine(transcodeFolderPath, file);

            var normalizedPlaylistId = playlistId;

            var playlistPath = _fileSystem.GetFilePaths(transcodeFolderPath)
                .FirstOrDefault(i =>
                    string.Equals(Path.GetExtension(i), ".m3u8", StringComparison.OrdinalIgnoreCase)
                    && i.IndexOf(normalizedPlaylistId, StringComparison.OrdinalIgnoreCase) != -1);

            return GetFileResult(file, playlistPath);
        }

        private ActionResult GetFileResult(string path, string playlistPath)
        {
            var transcodingJob = _transcodingJobHelper.OnTranscodeBeginRequest(playlistPath, TranscodingJobType.Hls);

            Response.OnCompleted(() =>
            {
                if (transcodingJob != null)
                {
                    _transcodingJobHelper.OnTranscodeEndRequest(transcodingJob);
                }

                return Task.CompletedTask;
            });

            return FileStreamResponseHelpers.GetStaticFileResult(path, MimeTypes.GetMimeType(path)!, false, this);
        }
    }
}