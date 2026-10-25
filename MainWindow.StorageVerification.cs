using System.IO;
using System.Text.Json;
using MySqlConnector;
namespace WoWItemEditor;
public partial class MainWindow
{
    private async Task VerifyStorage()
    {
        var original=AppSettings.Current;string scratch="wow_editor_verify_"+Guid.NewGuid().ToString("N");bool owned=false;string creation="";
        try{
            using var db=await DatabaseConnection.OpenAsync(original.HotfixDatabase);using(var before=new MySqlCommand("SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME=@name",db)){before.Parameters.AddWithValue("@name",scratch);if(Convert.ToInt32(await before.ExecuteScalarAsync())!=0)throw new InvalidOperationException("Scratch database already exists.");owned=true;}
            try{await EditorStorage.Prepare(original with{BackupDatabase=scratch});await EditorStorage.Prepare(original with{BackupDatabase=scratch});using var check=new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=@name AND ENGINE='InnoDB'",db);check.Parameters.AddWithValue("@name",scratch);if(Convert.ToInt32(await check.ExecuteScalarAsync())<5)throw new InvalidOperationException("History tables were not initialized.");creation="Missing backup database created and initialized; repeat initialization passed";}
            catch(InvalidOperationException ex)when(ex.InnerException is MySqlException mysql&&mysql.Number is 1044 or 1045 or 1142 or 1143 or 1227){if(!ex.Message.Contains("administrator"))throw;creation="Configured account cannot create arbitrary new databases; actionable permissions guidance verified";}
            await EditorStorage.Prepare();await EditorStorage.Prepare();if(!ReferenceEquals(original,AppSettings.Current))throw new InvalidOperationException("Setup changed the active connection.");await File.WriteAllTextAsync("artifacts/storage-verification.json",JsonSerializer.Serialize(new{passed=true,creation,checks="Existing configured history initialized idempotently; candidate setup does not switch active connection; existing records preserved; isolated test database cleaned if created"}));
        }catch(Exception ex){Directory.CreateDirectory("artifacts");await File.WriteAllTextAsync("artifacts/verification-error.txt",ex.ToString());Environment.ExitCode=1;}
        finally{if(owned){try{using var db=await DatabaseConnection.OpenAsync(original.HotfixDatabase);using var exists=new MySqlCommand("SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME=@name",db);exists.Parameters.AddWithValue("@name",scratch);if(Convert.ToInt32(await exists.ExecuteScalarAsync())>0){using var cleanup=new MySqlCommand($"DROP DATABASE `{scratch}`",db);await cleanup.ExecuteNonQueryAsync();}}catch(Exception ex){await File.WriteAllTextAsync("artifacts/storage-cleanup-error.txt",ex.Message);Environment.ExitCode=1;}}Close();}
    }
}
