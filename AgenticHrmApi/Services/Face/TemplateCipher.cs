using System.Security.Cryptography;

namespace AgenticHrmApi.Services.Face;

public class TemplateCipher
{
    private readonly byte[] _key;

    public TemplateCipher(string base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new ArgumentException("FaceEncryptionKey is missing.");

        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32)
            throw new ArgumentException("FaceEncryptionKey must be exactly 32 bytes.");
    }

    public void Encrypt(float[] embedding, out byte[] ciphertext, out byte[] nonce, out byte[] tag)
    {
        byte[] plaintext = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, plaintext, 0, plaintext.Length);

        nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        ciphertext = new byte[plaintext.Length];
        tag = new byte[16];

        using var aes = new AesGcm(_key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
    }

    public float[] Decrypt(byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        byte[] plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, tagSizeInBytes: 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        float[] embedding = new float[plaintext.Length / sizeof(float)];
        Buffer.BlockCopy(plaintext, 0, embedding, 0, plaintext.Length);
        return embedding;
    }
}
