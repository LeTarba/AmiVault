using System.Security.Cryptography;

namespace Acr122NfcReader;

internal static class AmiiboDumpPreparer
{
    private const int FullDumpSize = 540;
    private const int CoreSize = 520;
    private const int UuidSize = 7;
    private const int UuidOffset = 468;
    private const int PasswordOffset = 532;

    public static PreparedAmiiboDump Prepare(string binPath, byte[] tagUid, AmiiboKeySet keySet)
    {
        if (tagUid.Length != UuidSize)
        {
            throw new InvalidOperationException("Le tag doit exposer un UID sur 7 octets pour un amiibo.");
        }

        var source = File.ReadAllBytes(binPath);
        if (source.Length != FullDumpSize)
        {
            throw new InvalidOperationException(
                $"Le fichier amiibo doit faire {FullDumpSize} octets, taille detectee: {source.Length}.");
        }

        var prepared = (byte[])source.Clone();
        var plainCore = DecryptCore(source.AsSpan(0, CoreSize).ToArray(), keySet);

        ApplyUidPatch(plainCore, tagUid);
        ApplyPasswordAndDefaults(prepared, plainCore, tagUid);

        var encryptedCore = EncryptCore(plainCore, keySet);
        Buffer.BlockCopy(encryptedCore, 0, prepared, 0, CoreSize);

        return new PreparedAmiiboDump(
            binPath,
            Path.GetFileNameWithoutExtension(binPath),
            prepared,
            tagUid.ToArray(),
            BuildAmiiboPassword(tagUid));
    }

    private static void ApplyUidPatch(byte[] plainCore, byte[] tagUid)
    {
        var bcc0 = (byte)(0x88 ^ tagUid[0] ^ tagUid[1] ^ tagUid[2]);
        var bcc1 = (byte)(tagUid[3] ^ tagUid[4] ^ tagUid[5] ^ tagUid[6]);

        plainCore[UuidOffset + 0] = tagUid[0];
        plainCore[UuidOffset + 1] = tagUid[1];
        plainCore[UuidOffset + 2] = tagUid[2];
        plainCore[UuidOffset + 3] = bcc0;
        plainCore[UuidOffset + 4] = tagUid[3];
        plainCore[UuidOffset + 5] = tagUid[4];
        plainCore[UuidOffset + 6] = tagUid[5];
        plainCore[UuidOffset + 7] = tagUid[6];

        plainCore[0] = bcc1;
        plainCore[2] = 0x00;
        plainCore[3] = 0x00;
    }

    private static void ApplyPasswordAndDefaults(byte[] prepared, byte[] plainCore, byte[] tagUid)
    {
        var password = BuildAmiiboPassword(tagUid);
        Buffer.BlockCopy(password, 0, prepared, PasswordOffset, password.Length);

        prepared[520] = 0x00;
        prepared[521] = 0x00;
        prepared[522] = 0x00;
        prepared[536] = 0x80;
        prepared[537] = 0x80;
    }

    private static byte[] BuildAmiiboPassword(byte[] tagUid)
    {
        return
        [
            (byte)(0xAA ^ tagUid[1] ^ tagUid[3]),
            (byte)(0x55 ^ tagUid[2] ^ tagUid[4]),
            (byte)(0xAA ^ tagUid[3] ^ tagUid[5]),
            (byte)(0x55 ^ tagUid[4] ^ tagUid[6])
        ];
    }

    public static AmiiboKeySet LoadKeysFromFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new InvalidOperationException("Aucun dossier de cles amiibo n'est configure.");
        }

        if (!Directory.Exists(folderPath))
        {
            throw new InvalidOperationException($"Le dossier de cles amiibo est introuvable: {folderPath}");
        }

        var retailPath = Path.Combine(folderPath, "key_retail.bin");
        if (File.Exists(retailPath))
        {
            var retail = File.ReadAllBytes(retailPath);
            if (retail.Length != 160)
            {
                throw new InvalidOperationException("Le fichier key_retail.bin doit faire 160 octets.");
            }

            return new AmiiboKeySet(
                ParseCryptoMaterial(retail.AsSpan(0, 80).ToArray(), "unfixed-info"),
                ParseCryptoMaterial(retail.AsSpan(80, 80).ToArray(), "locked-secret"),
                retailPath,
                "key_retail.bin");
        }

        var unfixedPath = Path.Combine(folderPath, "unfixed-info.bin");
        var lockedPath = Path.Combine(folderPath, "locked-secret.bin");
        if (!File.Exists(unfixedPath) || !File.Exists(lockedPath))
        {
            throw new InvalidOperationException(
                "Impossible de trouver key_retail.bin ou le couple unfixed-info.bin + locked-secret.bin.");
        }

        return new AmiiboKeySet(
            ParseCryptoMaterial(File.ReadAllBytes(unfixedPath), "unfixed-info"),
            ParseCryptoMaterial(File.ReadAllBytes(lockedPath), "locked-secret"),
            folderPath,
            "unfixed-info.bin + locked-secret.bin");
    }

    private static AmiiboCryptoMaterial ParseCryptoMaterial(byte[] bytes, string label)
    {
        if (bytes.Length != 80)
        {
            throw new InvalidOperationException($"Le fichier {label} doit faire 80 octets.");
        }

        var typeStringEnd = Array.IndexOf(bytes, (byte)0x00, 16);
        if (typeStringEnd < 0)
        {
            throw new InvalidOperationException($"Le bloc {label} ne contient pas de type string valide.");
        }

        return new AmiiboCryptoMaterial(
            bytes.AsSpan(0, 16).ToArray(),
            bytes.AsSpan(16, (typeStringEnd - 16) + 1).ToArray(),
            bytes[31],
            bytes.AsSpan(32, 16).ToArray(),
            bytes.AsSpan(48, 32).ToArray());
    }

    private static byte[] DecryptCore(byte[] encryptedTag, AmiiboKeySet keySet)
    {
        var internalBuffer = new byte[CoreSize];
        Buffer.BlockCopy(encryptedTag, 0x008, internalBuffer, 0x000, 0x008);
        Buffer.BlockCopy(encryptedTag, 0x080, internalBuffer, 0x008, 0x020);
        Buffer.BlockCopy(encryptedTag, 0x010, internalBuffer, 0x028, 0x024);
        Buffer.BlockCopy(encryptedTag, 0x0A0, internalBuffer, 0x04C, 0x168);
        Buffer.BlockCopy(encryptedTag, 0x034, internalBuffer, 0x1B4, 0x020);
        Buffer.BlockCopy(encryptedTag, 0x000, internalBuffer, 0x1D4, 0x008);
        Buffer.BlockCopy(encryptedTag, 0x054, internalBuffer, 0x1DC, 0x02C);

        var seed = CalculateSeed(internalBuffer);
        var dataKeys = DeriveKeys(keySet.UnfixedInfo, seed);
        var tagKeys = DeriveKeys(keySet.LockedSecret, seed);

        var plain = new byte[CoreSize];
        TransformCtr(internalBuffer, 0x02C, 0x188, dataKeys.AesKey, dataKeys.AesIv, plain, 0x02C);
        Buffer.BlockCopy(internalBuffer, 0x000, plain, 0x000, 0x008);
        Buffer.BlockCopy(internalBuffer, 0x028, plain, 0x028, 0x004);
        Buffer.BlockCopy(internalBuffer, 0x1D4, plain, 0x1D4, 0x034);

        ComputeHmac(tagKeys.HmacKey, plain, 0x1D4, 0x034).CopyTo(plain, 0x1B4);
        ComputeHmac(dataKeys.HmacKey, plain, 0x029, 0x1DF).CopyTo(plain, 0x008);
        return plain;
    }

    private static byte[] EncryptCore(byte[] plain, AmiiboKeySet keySet)
    {
        var seed = CalculateSeed(plain);
        var dataKeys = DeriveKeys(keySet.UnfixedInfo, seed);
        var tagKeys = DeriveKeys(keySet.LockedSecret, seed);

        var cipherInternal = new byte[CoreSize];
        ComputeHmac(tagKeys.HmacKey, plain, 0x1D4, 0x034).CopyTo(cipherInternal, 0x1B4);

        using (var dataHmac = new HMACSHA256(dataKeys.HmacKey))
        {
            dataHmac.TransformBlock(plain, 0x029, 0x18B, null, 0);
            dataHmac.TransformBlock(cipherInternal, 0x1B4, 0x020, null, 0);
            dataHmac.TransformFinalBlock(plain, 0x1D4, 0x034);
            Buffer.BlockCopy(dataHmac.Hash!, 0, cipherInternal, 0x008, 0x020);
        }

        TransformCtr(plain, 0x02C, 0x188, dataKeys.AesKey, dataKeys.AesIv, cipherInternal, 0x02C);
        Buffer.BlockCopy(plain, 0x000, cipherInternal, 0x000, 0x008);
        Buffer.BlockCopy(plain, 0x028, cipherInternal, 0x028, 0x004);
        Buffer.BlockCopy(plain, 0x1D4, cipherInternal, 0x1D4, 0x034);

        var tag = new byte[CoreSize];
        Buffer.BlockCopy(cipherInternal, 0x000, tag, 0x008, 0x008);
        Buffer.BlockCopy(cipherInternal, 0x008, tag, 0x080, 0x020);
        Buffer.BlockCopy(cipherInternal, 0x028, tag, 0x010, 0x024);
        Buffer.BlockCopy(cipherInternal, 0x04C, tag, 0x0A0, 0x168);
        Buffer.BlockCopy(cipherInternal, 0x1B4, tag, 0x034, 0x020);
        Buffer.BlockCopy(cipherInternal, 0x1D4, tag, 0x000, 0x008);
        Buffer.BlockCopy(cipherInternal, 0x1DC, tag, 0x054, 0x02C);
        return tag;
    }

    private static byte[] CalculateSeed(byte[] dump)
    {
        var seed = new byte[64];
        Buffer.BlockCopy(dump, 0x029, seed, 0x000, 0x002);
        Array.Clear(seed, 0x002, 0x00E);
        Buffer.BlockCopy(dump, 0x1D4, seed, 0x010, 0x008);
        Buffer.BlockCopy(dump, 0x1D4, seed, 0x018, 0x008);
        Buffer.BlockCopy(dump, 0x1E8, seed, 0x020, 0x020);
        return seed;
    }

    private static DerivedKeys DeriveKeys(AmiiboCryptoMaterial material, byte[] baseSeed)
    {
        var preparedSeed = PrepareSeed(
            material.TypeString,
            material.MagicBytes,
            material.MagicBytesSize,
            material.XorPad,
            baseSeed);
        var derived = DrbgGenerateBytes(material.HmacKey, preparedSeed, 48);
        return new DerivedKeys(
            derived.Take(16).ToArray(),
            derived.Skip(16).Take(16).ToArray(),
            derived.Skip(32).Take(16).ToArray());
    }

    private static byte[] PrepareSeed(
        byte[] typeString,
        byte[] magicBytes,
        int magicBytesSize,
        byte[] xorPad,
        byte[] baseSeed)
    {
        var output = new byte[480];
        var offset = 0;
        var typeLength = Array.IndexOf(typeString, (byte)0x00);
        typeLength = typeLength >= 0 ? typeLength + 1 : typeString.Length;

        Buffer.BlockCopy(typeString, 0, output, offset, typeLength);
        offset += typeLength;

        var leadingSeedBytes = 16 - magicBytesSize;
        Buffer.BlockCopy(baseSeed, 0, output, offset, leadingSeedBytes);
        offset += leadingSeedBytes;

        Buffer.BlockCopy(magicBytes, 0, output, offset, magicBytesSize);
        offset += magicBytesSize;

        Buffer.BlockCopy(baseSeed, 0x10, output, offset, 16);
        offset += 16;

        for (var i = 0; i < 32; i++)
        {
            output[offset + i] = (byte)(baseSeed[0x20 + i] ^ xorPad[i]);
        }

        offset += 32;
        return output.Take(offset).ToArray();
    }

    private static byte[] DrbgGenerateBytes(byte[] hmacKey, byte[] seed, int outputSize)
    {
        var result = new byte[outputSize];
        var offset = 0;
        ushort iteration = 0;

        while (offset < outputSize)
        {
            var iterBuffer = new byte[2 + seed.Length];
            iterBuffer[0] = (byte)(iteration >> 8);
            iterBuffer[1] = (byte)(iteration & 0xFF);
            Buffer.BlockCopy(seed, 0, iterBuffer, 2, seed.Length);

            var output = HMACSHA256.HashData(hmacKey, iterBuffer);
            var toCopy = Math.Min(32, outputSize - offset);
            Buffer.BlockCopy(output, 0, result, offset, toCopy);
            offset += toCopy;
            iteration++;
        }

        return result;
    }

    private static byte[] ComputeHmac(byte[] key, byte[] data, int offset, int count)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data, offset, count);
    }

    private static void TransformCtr(
        byte[] input,
        int inputOffset,
        int count,
        byte[] key,
        byte[] iv,
        byte[] output,
        int outputOffset)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        aes.IV = new byte[16];

        using var encryptor = aes.CreateEncryptor();
        var counter = iv.ToArray();
        var keystream = new byte[16];
        var temp = new byte[16];
        var remaining = count;
        var sourceIndex = inputOffset;
        var destinationIndex = outputOffset;

        while (remaining > 0)
        {
            encryptor.TransformBlock(counter, 0, 16, keystream, 0);
            var blockSize = Math.Min(16, remaining);
            for (var i = 0; i < blockSize; i++)
            {
                temp[i] = (byte)(input[sourceIndex + i] ^ keystream[i]);
            }

            Buffer.BlockCopy(temp, 0, output, destinationIndex, blockSize);
            IncrementCounter(counter);
            sourceIndex += blockSize;
            destinationIndex += blockSize;
            remaining -= blockSize;
        }
    }

    private static void IncrementCounter(byte[] counter)
    {
        for (var i = counter.Length - 1; i >= 0; i--)
        {
            counter[i]++;
            if (counter[i] != 0)
            {
                break;
            }
        }
    }

    internal sealed record PreparedAmiiboDump(
        string SourcePath,
        string DisplayName,
        byte[] EncryptedPages,
        byte[] TagUid,
        byte[] AmiiboPassword);

    internal sealed record AmiiboKeySet(
        AmiiboCryptoMaterial UnfixedInfo,
        AmiiboCryptoMaterial LockedSecret,
        string SourcePath,
        string SourceDescription);

    internal sealed record AmiiboCryptoMaterial(
        byte[] HmacKey,
        byte[] TypeString,
        int MagicBytesSize,
        byte[] MagicBytes,
        byte[] XorPad);

    private sealed record DerivedKeys(byte[] AesKey, byte[] AesIv, byte[] HmacKey);
}
