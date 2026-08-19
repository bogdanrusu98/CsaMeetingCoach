using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CsaMeetingCoach.Core;

public sealed class InMemorySessionJoinCodeStore : ISessionJoinCodeStore
{
    private readonly ConcurrentDictionary<Guid, JoinCodeRecord> _records = new();
    private readonly ConcurrentDictionary<string, Guid> _lookupIndex =
        new(StringComparer.Ordinal);
    private readonly byte[] _lookupKey = RandomNumberGenerator.GetBytes(32);
    private readonly object _creationLock = new();

    public Task<string> CreateAsync(
        Guid sessionId,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new SessionExpiredException(sessionId);
        }

        lock (_creationLock)
        {
            if (_records.TryRemove(sessionId, out var existing))
            {
                if (!string.IsNullOrWhiteSpace(existing.LookupHash))
                {
                    _lookupIndex.TryRemove(existing.LookupHash, out _);
                }
            }

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var (code, record) = SessionJoinCode.Create(
                    sessionId,
                    expiresAtUtc,
                    _lookupKey);
                if (!_lookupIndex.TryAdd(record.LookupHash, sessionId))
                {
                    continue;
                }

                _records[sessionId] = record;
                return Task.FromResult(code);
            }
        }

        throw new InvalidOperationException("A unique session code could not be generated.");
    }

    public Task<Guid?> ResolveAsync(string code, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = SessionJoinCode.Normalize(code);
        var lookupHash = SessionJoinCode.ComputeLookupHash(normalized, _lookupKey);
        if (!_lookupIndex.TryGetValue(lookupHash, out var sessionId)
            || !_records.TryGetValue(sessionId, out var record)
            || record.ExpiresAtUtc <= DateTimeOffset.UtcNow
            || !SessionJoinCode.Matches(normalized, record))
        {
            return Task.FromResult<Guid?>(null);
        }

        return Task.FromResult<Guid?>(sessionId);
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_records.TryRemove(sessionId, out var record))
        {
            if (!string.IsNullOrWhiteSpace(record.LookupHash))
            {
                _lookupIndex.TryRemove(record.LookupHash, out _);
            }
        }

        return Task.CompletedTask;
    }
}

public sealed class JsonSessionJoinCodeStore(string dataDirectory) : ISessionJoinCodeStore
{
    private const string LookupKeyFileName = ".lookup-key";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly string _directory = Path.Combine(dataDirectory, "access");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Guid> _lookupIndex = new(StringComparer.Ordinal);
    private byte[]? _lookupKey;
    private bool _indexLoaded;

    public async Task<string> CreateAsync(
        Guid sessionId,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken)
    {
        if (expiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new SessionExpiredException(sessionId);
        }

        await _gate.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            var existing = await ReadRecordAsync(sessionId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(existing?.LookupHash))
            {
                _lookupIndex.Remove(existing.LookupHash);
            }

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var (code, record) = SessionJoinCode.Create(
                    sessionId,
                    expiresAtUtc,
                    _lookupKey!);
                if (_lookupIndex.ContainsKey(record.LookupHash))
                {
                    continue;
                }

                var path = GetPath(sessionId);
                temporaryPath = string.Concat(
                    path,
                    ".",
                    Guid.NewGuid().ToString("N"),
                    ".tmp");
                await using (var stream = File.Create(temporaryPath))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        record,
                        JsonOptions,
                        cancellationToken);
                }

                File.Move(temporaryPath, path, overwrite: true);
                temporaryPath = null;
                _lookupIndex.Add(record.LookupHash, sessionId);
                return code;
            }

            throw new InvalidOperationException("A unique session code could not be generated.");
        }
        finally
        {
            try
            {
                if (temporaryPath is not null && File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public async Task<Guid?> ResolveAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var normalized = SessionJoinCode.Normalize(code);
        if (!Directory.Exists(_directory))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            var lookupHash = SessionJoinCode.ComputeLookupHash(normalized, _lookupKey!);
            if (!_lookupIndex.TryGetValue(lookupHash, out var sessionId))
            {
                return null;
            }

            var record = await ReadRecordAsync(sessionId, cancellationToken);
            if (record is null
                || !string.Equals(record.LookupHash, lookupHash, StringComparison.Ordinal)
                || record.ExpiresAtUtc <= DateTimeOffset.UtcNow
                || !SessionJoinCode.Matches(normalized, record))
            {
                return null;
            }

            return sessionId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            var record = await ReadRecordAsync(sessionId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(record?.LookupHash))
            {
                _lookupIndex.Remove(record.LookupHash);
            }

            foreach (var temporaryPath in Directory.EnumerateFiles(
                _directory,
                $"{sessionId:N}.json.*.tmp"))
            {
                File.Delete(temporaryPath);
            }

            var path = GetPath(sessionId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_indexLoaded)
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        var keyPath = Path.Combine(_directory, LookupKeyFileName);
        foreach (var temporaryKeyPath in Directory.EnumerateFiles(
            _directory,
            string.Concat(LookupKeyFileName, ".*.tmp")))
        {
            File.Delete(temporaryKeyPath);
        }

        if (File.Exists(keyPath))
        {
            _lookupKey = await File.ReadAllBytesAsync(keyPath, cancellationToken);
            if (_lookupKey.Length != 32)
            {
                throw new InvalidDataException(
                    "The session-code lookup key has an invalid length.");
            }
        }
        else
        {
            _lookupKey = RandomNumberGenerator.GetBytes(32);
            var temporaryKeyPath = string.Concat(
                keyPath,
                ".",
                Guid.NewGuid().ToString("N"),
                ".tmp");
            try
            {
                await using (var keyStream = new FileStream(
                    temporaryKeyPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await keyStream.WriteAsync(_lookupKey, cancellationToken);
                    await keyStream.FlushAsync(cancellationToken);
                    keyStream.Flush(flushToDisk: true);
                }

                File.Move(temporaryKeyPath, keyPath);
                if (OperatingSystem.IsWindows())
                {
                    File.SetAttributes(keyPath, FileAttributes.Hidden);
                }
            }
            finally
            {
                if (File.Exists(temporaryKeyPath))
                {
                    File.Delete(temporaryKeyPath);
                }
            }
        }

        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(path);
            var record = await JsonSerializer.DeserializeAsync<JoinCodeRecord>(
                stream,
                JsonOptions,
                cancellationToken);
            if (record is null || string.IsNullOrWhiteSpace(record.LookupHash))
            {
                continue;
            }

            if (!_lookupIndex.TryAdd(record.LookupHash, record.SessionId))
            {
                throw new InvalidDataException(
                    "Duplicate session-code lookup hashes were found.");
            }
        }

        _indexLoaded = true;
    }

    private async Task<JoinCodeRecord?> ReadRecordAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var path = GetPath(sessionId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<JoinCodeRecord>(
            stream,
            JsonOptions,
            cancellationToken);
    }

    private string GetPath(Guid sessionId) =>
        Path.Combine(_directory, $"{sessionId:N}.json");
}

public sealed record JoinCodeRecord(
    Guid SessionId,
    string LookupHash,
    string Salt,
    string Hash,
    int Iterations,
    DateTimeOffset ExpiresAtUtc);

internal static class SessionJoinCode
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const int CodeLength = 8;
    private const int SaltLength = 16;
    private const int HashLength = 32;
    private const int Iterations = 100_000;

    public static (string Code, JoinCodeRecord Record) Create(
        Guid sessionId,
        DateTimeOffset expiresAtUtc,
        byte[] lookupKey)
    {
        Span<byte> random = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(random);
        Span<char> characters = stackalloc char[CodeLength];
        for (var index = 0; index < random.Length; index++)
        {
            characters[index] = Alphabet[random[index] % Alphabet.Length];
        }

        var normalized = new string(characters);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            normalized,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashLength);
        var code = string.Concat(normalized.AsSpan(0, 4), "-", normalized.AsSpan(4, 4));
        return (
            code,
            new JoinCodeRecord(
                sessionId,
                ComputeLookupHash(normalized, lookupKey),
                Convert.ToBase64String(salt),
                Convert.ToBase64String(hash),
                Iterations,
                expiresAtUtc));
    }

    public static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Session code is required.", nameof(code));
        }

        var normalized = new string(code
            .Where(character => character != '-' && !char.IsWhiteSpace(character))
            .Select(char.ToUpperInvariant)
            .ToArray());
        if (normalized.Length != CodeLength
            || normalized.Any(character => !Alphabet.Contains(character)))
        {
            throw new ArgumentException("Session code must contain eight valid characters.", nameof(code));
        }

        return normalized;
    }

    public static string ComputeLookupHash(string normalizedCode, byte[] lookupKey)
    {
        var hash = HMACSHA256.HashData(
            lookupKey,
            Encoding.ASCII.GetBytes(normalizedCode));
        return Convert.ToHexString(hash);
    }

    public static bool Matches(string normalizedCode, JoinCodeRecord record)
    {
        if (record.Iterations != Iterations)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(record.Salt);
            var expected = Convert.FromBase64String(record.Hash);
            if (salt.Length != SaltLength || expected.Length != HashLength)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                normalizedCode,
                salt,
                record.Iterations,
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
