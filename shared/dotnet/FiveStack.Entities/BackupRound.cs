namespace FiveStack.Entities;

public class BackupRound
{
    public int round { get; set; }
    public string deleted_at { get; set; } = "";
    public string backup_file { get; set; } = "";

    // Found on this server's disk, so the backend has never heard of it.
    public bool from_disk { get; set; }
}
