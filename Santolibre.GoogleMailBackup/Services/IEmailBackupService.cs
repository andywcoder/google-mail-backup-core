namespace Santolibre.GoogleMailBackup.Services
{
    public interface IGoogleMailBackupService
    {
        void ExportEmails(string query, string exportPath);
    }
}
