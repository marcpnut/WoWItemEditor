using MySqlConnector;
namespace WoWItemEditor;
internal static class EditorStorage
{
    internal static async Task Prepare(EditorSettings? settings=null)
    {
        var cfg=settings??AppSettings.Current;cfg.Validate(false);
        try{using var db=new MySqlConnection(AppSettings.Connection(cfg,cfg.HotfixDatabase).ConnectionString);await db.OpenAsync();await ContentEditing.InitializeAsync(db,cfg);}
        catch(MySqlException ex)when(ex.Number is 1044 or 1045 or 1142 or 1143 or 1227){throw new InvalidOperationException($"The MariaDB user cannot prepare editor history. Ask your database administrator for CREATE and normal read/write permissions on {cfg.BackupDatabase}, and permission to create the editor audit table in {cfg.HotfixDatabase}. Save the corrected connection in Settings and restart. Catalogs can still be browsed; saves keep their history checks.",ex);}
    }
}
