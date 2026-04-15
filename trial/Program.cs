using Samparse;

var samDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../SAM"));
var db = SamLoader.Load(samDir);

Console.WriteLine("Done. Press Enter to exit.");

Console.ReadLine();
