// LPAP.Audio/AudioCollection.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LPAP.Audio
{
    public class AudioCollection : IDisposable
    {
        private readonly Dictionary<Guid, AudioObj> _byId = [];

        public BindingList<AudioObj> Items { get; } = [];

        public AudioObj? this[Guid id] =>
            this._byId.TryGetValue(id, out var obj) ? obj : null;

        public AudioObj this[int index] => this.Items[index];

        public async Task<IReadOnlyList<AudioObj>> AddFromFilesAsync(
            IEnumerable<string> filePaths,
            int? maxParallelImports = null,
            CancellationToken ct = default)
        {
            maxParallelImports ??= Environment.ProcessorCount;
            maxParallelImports = Math.Clamp(maxParallelImports.Value, 1, Environment.ProcessorCount);

            var results = new List<AudioObj>();
            var tasks = new List<Task>();

            using var sem = new SemaphoreSlim(maxParallelImports.Value);

            foreach (var path in filePaths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                await sem.WaitAsync(ct).ConfigureAwait(false);

                var t = Task.Run(async () =>
                {
                    try
                    {
                        var obj = new AudioObj();
                        await obj.LoadFromFileAsync(path, ct: ct).ConfigureAwait(false);
                        this.Add(obj);
                        lock (results)
                        {
                            results.Add(obj);
                        }
                    }
                    finally
                    {
                        sem.Release();
                    }
                }, ct);

                tasks.Add(t);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return results;
        }

        public void Add(AudioObj obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            this.Items.Add(obj);
            this._byId[obj.Id] = obj;
        }

        public bool Update(AudioObj obj, bool cloneOriginal = true)
        {
            if (obj == null)
            {
                return false;
            }

            if (this._byId.ContainsKey(obj.Id))
            {
                if (cloneOriginal)
                {
                    this._byId[obj.Id] = obj.Clone();
                }
                else
                {
                    this._byId[obj.Id] = obj;
                }

                return true;
            }
            else
            {
                if (cloneOriginal)
                {
                    this.Add(obj.Clone());
                }
                else
                {
                    this.Add(obj);
                }
            }

            return false;
        }

        public bool Remove(AudioObj obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (this.Items.Remove(obj))
            {
                this._byId.Remove(obj.Id);
                obj.Dispose();
                return true;
            }

            return false;
        }

        public bool RemoveWithoutDispose(AudioObj obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (this.Items.Remove(obj))
            {
                this._byId.Remove(obj.Id);
                // Kein obj.Dispose() hier!
                return true;
            }

            return false;
        }




        public bool Remove(Guid id)
        {
            var obj = this[id];
            return obj != null && this.Remove(obj);
        }

        public void Clear()
        {
            foreach (var obj in this.Items)
            {
                obj.Dispose();
            }

            this.Items.Clear();
            this._byId.Clear();
        }

        public void Dispose()
        {
            this.Clear();
        }
    }
}
