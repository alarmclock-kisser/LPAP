using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LPAP.OpenVino.Util
{
	public sealed class HuggingfaceBrowser
	{
		private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

		private readonly HttpClient _http;

		/// <summary>Default: D:\Models\</summary>
		public string DownloadDirectory { get; set; } = @"D:\Models\";

		/// <summary>Optional: HuggingFace token for gated/private repos (hf_...).</summary>
		public string? HuggingFaceToken { get; set; }

		public HuggingfaceBrowser(HttpClient? httpClient = null, int timeoutMinutes = 30)
		{
			this._http = httpClient ?? new HttpClient();
			this._http.Timeout = TimeSpan.FromMinutes(timeoutMinutes);
			this._http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LPAP.OpenVino", "1.0"));
		}

        public async Task<IReadOnlyList<HfModelSearchResult>> SearchModelsAsync(
            string? search = null,
            bool onlyAudioSourceSeparation = true,
            int limit = 50,
            string sort = "downloads",
            int direction = -1,
            string[]? extensions = null,
            CancellationToken ct = default)
		{
			limit = Math.Clamp(limit, 1, 200);

			// Hub API endpoints are documented and open. :contentReference[oaicite:3]{index=3}
			// Example filter used by UI: audio-source-separation :contentReference[oaicite:4]{index=4}
			var url =
				"https://huggingface.co/api/models" +
				$"?limit={limit}" +
				$"&sort={Uri.EscapeDataString(sort)}" +
				$"&direction={direction}";

			if (!string.IsNullOrWhiteSpace(search))
			{
				url += $"&search={Uri.EscapeDataString(search)}";
			}

			if (onlyAudioSourceSeparation)
			{
				url += $"&filter=audio-source-separation";
			}

			using var req = new HttpRequestMessage(HttpMethod.Get, url);
			this.AddAuth(req);

			using var resp = await this._http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
			resp.EnsureSuccessStatusCode();

			await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var data = await JsonSerializer.DeserializeAsync<List<HfModelSearchResult>>(s, JsonOpts, ct).ConfigureAwait(false);

            var results = data ?? [];

            // Optional filter by extensions present in repo files
            if (extensions is { Length: > 0 })
            {
                var exts = extensions
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(e => e.StartsWith('.') ? e : "." + e)
                    .Select(e => e.ToLowerInvariant())
                    .ToArray();

                var filtered = new List<HfModelSearchResult>(results.Count);
                foreach (var r in results)
                {
                    ct.ThrowIfCancellationRequested();
                    var id = r.ModelId;
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    try
                    {
                        var files = await this.GetModelFilesAsync(id!, ct).ConfigureAwait(false);
                        bool match = files.Any(f =>
                            f?.RFilename is string rf &&
                            exts.Any(ext => rf.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));
                        if (match)
                        {
                            filtered.Add(r);
                        }
                    }
                    catch
                    {
                        // ignore failure, skip
                    }
                }

                return filtered;
            }

            return results;
		}

		public async Task<HfModelInfo> GetModelInfoAsync(string repoId, CancellationToken ct = default)
		{
			if (string.IsNullOrWhiteSpace(repoId))
			{
				throw new ArgumentNullException(nameof(repoId));
			}

			// GET /api/models/{repoId}
			var url = $"https://huggingface.co/api/models/{repoId}";
			using var req = new HttpRequestMessage(HttpMethod.Get, url);
			this.AddAuth(req);

			using var resp = await this._http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
			resp.EnsureSuccessStatusCode();

			await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
			var info = await JsonSerializer.DeserializeAsync<HfModelInfo>(s, JsonOpts, ct).ConfigureAwait(false);
			return info ?? throw new InvalidOperationException("HF returned empty model info.");
		}

		public async Task<IReadOnlyList<HfSiblingFile>> GetModelFilesAsync(string repoId, CancellationToken ct = default)
		{
			var info = await this.GetModelInfoAsync(repoId, ct).ConfigureAwait(false);
			return info.Siblings ?? [];
		}

		/// <summary>
		/// Downloads selected files from a HF repo to {DownloadDirectory}\{repoId}\...
		/// Progress: 0..1 overall across all bytes (best-effort; some files may not provide size).
		/// </summary>
		public async Task<string> DownloadModelFilesAsync(
			string repoId,
			IEnumerable<string> filePaths,
			string revision = "main",
			IProgress<double>? progress = null,
			CancellationToken ct = default)
		{
			if (string.IsNullOrWhiteSpace(repoId))
			{
				throw new ArgumentNullException(nameof(repoId));
			}

			if (filePaths is null)
			{
				throw new ArgumentNullException(nameof(filePaths));
			}

			var files = filePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
			if (files.Count == 0)
			{
				throw new ArgumentException("No files specified.", nameof(filePaths));
			}

			var baseDir = Path.Combine(this.DownloadDirectory, SanitizeRepoId(repoId));
			Directory.CreateDirectory(baseDir);

			// Try to get sizes from model info (siblings). Note: sizes may be missing for some models. :contentReference[oaicite:5]{index=5}
			var info = await this.GetModelInfoAsync(repoId, ct).ConfigureAwait(false);
			var sizeMap = (info.Siblings ?? [])
				.Where(s => s is not null && !string.IsNullOrWhiteSpace(s.RFilename))
				.ToDictionary(s => s.RFilename!, s => s.SizeInBytes ?? -1L, StringComparer.Ordinal);

			long totalKnown = 0;
			foreach (var f in files)
			{
				if (sizeMap.TryGetValue(f, out var sz) && sz > 0)
				{
					totalKnown += sz;
				}
			}

			long downloadedKnown = 0;
			progress?.Report(0);

			for (int i = 0; i < files.Count; i++)
			{
				ct.ThrowIfCancellationRequested();

				var relPath = files[i].Replace('\\', '/');
				var localPath = Path.Combine(baseDir, relPath.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

				var url = $"https://huggingface.co/{repoId}/resolve/{revision}/{Uri.EscapeDataString(relPath)}";
				using var req = new HttpRequestMessage(HttpMethod.Get, url);
				this.AddAuth(req);

				using var resp = await this._http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
				resp.EnsureSuccessStatusCode();

				var contentLen = resp.Content.Headers.ContentLength;
				long fileKnown = contentLen is > 0 ? contentLen.Value :
								 sizeMap.TryGetValue(relPath, out var szz) && szz > 0 ? szz : -1L;

				await using var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
				await using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 256, useAsync: true);

				var buffer = new byte[1024 * 256];
				long fileRead = 0;

				while (true)
				{
					int n = await net.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
					if (n <= 0)
					{
						break;
					}

					await fs.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
					fileRead += n;

					if (totalKnown > 0 && fileKnown > 0)
					{
						// overall progress best-effort based on known bytes
						var overall = (downloadedKnown + Math.Min(fileRead, fileKnown)) / (double) totalKnown;
						progress?.Report(Math.Clamp(overall, 0, 0.999999));
					}
					else
					{
						// fallback: progress by file count
						var overall = (i + 0.25) / files.Count;
						progress?.Report(Math.Clamp(overall, 0, 0.999999));
					}
				}

				if (totalKnown > 0 && fileKnown > 0)
				{
					downloadedKnown += fileKnown;
				}

				// bump after each file
				if (totalKnown > 0)
				{
					progress?.Report(Math.Clamp(downloadedKnown / (double) totalKnown, 0, 0.999999));
				}
				else
				{
					progress?.Report(Math.Clamp((i + 1) / (double) files.Count, 0, 0.999999));
				}
			}

			progress?.Report(1.0);
			return baseDir;
		}

		/// <summary>
		/// Downloads OpenVINO IR (.xml + .bin) if present in repo.
		/// Returns local directory.
		/// </summary>
		public async Task<string> DownloadOpenVinoIrAsync(
			string repoId,
			string revision = "main",
			IProgress<double>? progress = null,
			CancellationToken ct = default)
		{
			var files = await this.GetModelFilesAsync(repoId, ct).ConfigureAwait(false);
			var xmls = files
				.Where(f => f?.RFilename is not null && f.RFilename.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
				.Select(f => f!.RFilename!)
				.ToList();

			if (xmls.Count == 0)
			{
				throw new InvalidOperationException($"Repo '{repoId}' contains no OpenVINO IR .xml files.");
			}

			// For each xml also download corresponding .bin if it exists
			var set = new HashSet<string>(StringComparer.Ordinal);
			foreach (var xml in xmls)
			{
				set.Add(xml);
				var bin = Path.ChangeExtension(xml, ".bin");
				if (files.Any(f => string.Equals(f?.RFilename, bin, StringComparison.Ordinal)))
				{
					set.Add(bin);
				}
			}

			return await this.DownloadModelFilesAsync(repoId, set, revision, progress, ct).ConfigureAwait(false);
		}

		private void AddAuth(HttpRequestMessage req)
		{
			if (!string.IsNullOrWhiteSpace(this.HuggingFaceToken))
			{
				req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.HuggingFaceToken);
			}
		}

		private static string SanitizeRepoId(string repoId)
		{
			// "org/name" -> "org__name" for folder name
			foreach (var c in Path.GetInvalidFileNameChars())
			{
				repoId = repoId.Replace(c, '_');
			}

			return repoId.Replace('/', '_').Replace('\\', '_');
		}

		// ---------------- DTOs ----------------

		public sealed record HfModelSearchResult
		{
			[JsonPropertyName("modelId")] public string? ModelId { get; init; }
			[JsonPropertyName("sha")] public string? Sha { get; init; }
			[JsonPropertyName("downloads")] public long? Downloads { get; init; }
			[JsonPropertyName("likes")] public long? Likes { get; init; }
			[JsonPropertyName("pipeline_tag")] public string? PipelineTag { get; init; }
			[JsonPropertyName("library_name")] public string? LibraryName { get; init; }
			[JsonPropertyName("tags")] public string[]? Tags { get; init; }
		}

		public sealed record HfModelInfo
		{
			[JsonPropertyName("modelId")] public string? ModelId { get; init; }
			[JsonPropertyName("sha")] public string? Sha { get; init; }
			[JsonPropertyName("pipeline_tag")] public string? PipelineTag { get; init; }
			[JsonPropertyName("tags")] public string[]? Tags { get; init; }

			// "siblings" is the file listing
			[JsonPropertyName("siblings")] public List<HfSiblingFile>? Siblings { get; init; }
		}

		public sealed record HfSiblingFile
		{
			[JsonPropertyName("rfilename")] public string? RFilename { get; init; }

			// size fields are not always present/filled for all models :contentReference[oaicite:6]{index=6}
			[JsonPropertyName("size")] public long? SizeInBytes { get; init; }
		}
	}
}
