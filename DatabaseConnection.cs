using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;
namespace AnoWowItemEditor;

internal static class DatabaseConnection
{
    private sealed record LoginInfo(string Server,uint Port,string User,string Password);
    internal static async Task<MySqlConnection> OpenAsync(string database)
    {
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AnoCoreItemEditorStudio","mariadb-login.dat");
        if(!File.Exists(path))throw new InvalidOperationException("No saved MariaDB login found. Save your connection in the previous ItemEditor app, then retry.");
        var login=JsonSerializer.Deserialize<LoginInfo>(Encoding.UTF8.GetString(ProtectedData.Unprotect(await File.ReadAllBytesAsync(path),null,DataProtectionScope.CurrentUser)))??throw new InvalidOperationException("Saved MariaDB login could not be read.");
        var cs=new MySqlConnectionStringBuilder {Server=login.Server,Port=login.Port,UserID=login.User,Password=login.Password,Database=database,ConnectionTimeout=8,SslMode=MySqlSslMode.Preferred};
        var db=new MySqlConnection(cs.ConnectionString);
        try{await db.OpenAsync();return db;}catch{await db.DisposeAsync();throw;}
    }
}
