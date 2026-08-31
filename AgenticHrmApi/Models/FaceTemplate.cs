using System;

namespace AgenticHrmApi.Models;

public class FaceTemplate
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public byte[] EncryptedEmbedding { get; set; } = [];  // AES-GCM ciphertext of 128 × float32
    public byte[] Nonce { get; set; } = [];               // 12 bytes, unique per row
    public byte[] Tag { get; set; } = [];                 // 16 bytes

    public string ModelVersion { get; set; } = "sface-2021dec";  // lets a future model coexist
    public string Pose { get; set; } = "frontal";                // frontal|yaw_left|yaw_right|up|down
    public float Quality { get; set; }                            // YuNet confidence at enrol time

    public int EnrolledByUserId { get; set; }             // the Admin who scanned. FROM THE JWT.
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
