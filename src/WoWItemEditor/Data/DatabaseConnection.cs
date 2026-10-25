using MySqlConnector;
namespace WoWItemEditor;
internal static class DatabaseConnection
{
    internal static async Task<MySqlConnection> OpenAsync(string database)
    {
        var settings=AppSettings.Current;settings.Validate(folders:false);var db=new MySqlConnection(AppSettings.Connection(settings,database).ConnectionString);
        try{await db.OpenAsync();return db;}catch{await db.DisposeAsync();throw;}
    }
}
