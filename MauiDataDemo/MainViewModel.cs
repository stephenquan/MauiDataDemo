// MainViewModel.cs

using System.Data;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SQLite;
using SQLitePCL;

namespace MauiDataDemo;

public partial class MainViewModel : ObservableObject
{
	public static SingleThreadDispatcher Dispatcher = new(nameof(MainViewModel));
	public static ILogger? Logger => field ??= IPlatformApplication.Current?.Services.GetService<ILogger<MainViewModel>>();
	public SQLiteConnection MyDatabase { get; }
	public DataTable MyDataTable { get; internal set; } = new();

	[ObservableProperty]
	public partial string SearchFilter { get; set; } = string.Empty;

	partial void OnSearchFilterChanged(string value)
	{
		RefreshCities();
	}

	[ObservableProperty]
	public partial List<CityInfo> Cities { get; internal set; } = new();

	public MainViewModel()
	{
		MyDatabase = new(":memory:");

		SQLitePCL.raw.sqlite3_create_function(
			MyDatabase.Handle,
			"GetNWord",
			2,
			SQLitePCL.raw.SQLITE_DETERMINISTIC,
			null,
			GetNWord);

		//MyDatabase.CreateTable<CityInfo>();

		MyDatabase.RunInTransaction(() =>
		{
			MyDatabase.Execute(
				"""
				CREATE TABLE Cities
				(
					Id INTEGER UNIQUE,
					City TEXT
				)
				""");
			MyDatabase.Execute("CREATE INDEX IX_Cities_001 ON Cities (Id)");
			MyDatabase.Execute("CREATE INDEX IX_Cities_002 ON Cities (GetNWord(City, 0))");
			MyDatabase.Execute("CREATE INDEX IX_Cities_003 ON Cities (GetNWord(City, 1))");
			MyDatabase.Execute("CREATE INDEX IX_Cities_004 ON Cities (GetNWord(City, 0), GetNWord(City, 1))");
		});
	}

	static string[] cityPrefixes = { "New", "Old", "North", "South", "East", "West" };
	static string[] cityNames = { "York", "Sydney", "Melbourne", "Tokyo", "London", "Paris", "Berlin", "Moscow", "Beijing", "Mumbai" };
	static string[] citySuffixes = { "ville", "town", "city", "polis", "grad", "burg" };

	public static string RandomCityName()
	{
		StringBuilder sb = new();
		sb.Append(cityPrefixes[Random.Shared.Next(cityPrefixes.Length)]);
		sb.Append(' ');
		sb.Append(cityNames[Random.Shared.Next(cityNames.Length)]);
		sb.Append(citySuffixes[Random.Shared.Next(citySuffixes.Length)]);
		return sb.ToString();
	}

	/// <summary>
	/// Traditional approach: perform all inserts within a single transaction on the calling thread.
	/// </summary>
	/// <returns></returns>
	[RelayCommand]
	public void PopulateSQLite1()
	{
		MyDatabase.RunInTransaction(() =>
		{
			MyDatabase.Execute("DELETE FROM CITIES");
			for (int i = 0; i < 100000; i++)
			{
				MyDatabase.Insert(new CityInfo { Id = i + 1, City = RandomCityName() });
			}
		});
		RefreshCities();
	}

	/// <summary>
	/// Unsafe approach: perform inserts in parallel on multiple threads without synchronisation, which can lead to database corruption and application crashes.
	/// </summary>
	[RelayCommand]
	public async Task PopulateSQLite2()
	{
		try
		{
			MyDatabase.Execute("DELETE FROM CITIES");
			List<Task> tasks = new();
			for (int i = 0; i < 10; i++)
			{
				int _i = i;
				var t = Task.Run(() =>
				{
					MyDatabase.Insert(new CityInfo { Id = _i + 1, City = RandomCityName() });
				});
				tasks.Add(t);
			}
			await Task.WhenAll(tasks);

			//var task1 = Task.Run(() =>
			//{
			//	for (int i = 0; i < 50000; i++)
			//	{
			//		MyDatabase.Insert(new CityInfo { Id = i + 1, City = RandomCityName() });
			//	}
			//});
			//var task2 = Task.Run(() =>
			//{
			//	for (int i = 50000; i < 100000; i++)
			//	{
			//		MyDatabase.Insert(new CityInfo { Id = i + 1, City = RandomCityName() });
			//	}
			//});
			//await Task.WhenAll(task1, task2);
			RefreshCities();
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Error populating database in parallel: {Message}", ex.Message);
		}
	}

	public void RefreshCities()
	{
		if (!string.IsNullOrEmpty(SearchFilter)
			&& SearchFilter.Split() is var words
			&& words is not null
			&& words.Length > 0)
		{
			if (words.Length == 1)
			{
				Cities = MyDatabase.Query<CityInfo>(
					$$"""
					SELECT *
					FROM	Cities c
					WHERE	EXISTS (
							SELECT 1
							FROM Cities c1
							WHERE GetNWord(c1.City, 0) LIKE ?
							AND c1.Id = c.Id)
					OR		EXISTS (
							SELECT 1
							FROM Cities c2
							WHERE GetNWord(c2.City, 1) LIKE ?
							AND c2.Id = c.Id)
					""",
					words[0] + "%",
					words[0] + "%");
				return;
			}

			Cities = MyDatabase.Query<CityInfo>(
				$$"""
				SELECT *
				FROM	Cities c
				WHERE	EXISTS (
						SELECT 1
						FROM Cities c1
						WHERE GetNWord(c1.City, 0) LIKE ?
						AND c1.Id = c.Id)
				AND		EXISTS (
						SELECT 1
						FROM Cities c2
						WHERE GetNWord(c2.City, 1) LIKE ?
						AND c2.Id = c.Id)
				""",
				words[0] + "%",
				words[1] + "%");
			return;
		}

		Cities = MyDatabase.Table<CityInfo>().ToList();
	}

	/// <summary>
	/// Implements GetNWord(text, wordIndex) for SQLite
	/// </summary>
	/// <param name="ctx">The SQLite context.</param>
	/// <param name="userData">User data passed to the function.</param>
	/// <param name="args">The arguments passed to the function.</param>
	public static void GetNWord(sqlite3_context ctx, object userData, sqlite3_value[] args)
	{
		if (args.Length < 2)
		{
			raw.sqlite3_result_error_code(ctx, (int)SQLite3.Result.Misuse);
			return;
		}

		string text = SQLitePCL.raw.sqlite3_value_text(args[0]).utf8_to_string();
		int wordIndex = SQLitePCL.raw.sqlite3_value_int(args[1]);

		string result = string.Empty;

		if (!string.IsNullOrEmpty(text)
			&& text.Split() is var words
			&& words is not null
			&& wordIndex >= 0 && wordIndex < words.Length)
		{
			result = words[wordIndex];
		}

		raw.sqlite3_result_text(ctx, result);
	}

	/// <summary>
	/// Populating on UI thread: perform all inserts on the UI thread, which is safe but can lead to a frozen UI and poor user experience.
	/// </summary>
	[RelayCommand]
	public void PopulateDataTable1()
	{
		MyDataTable = new DataTable();
		MyDataTable.Columns.Add("id", typeof(int));
		MyDataTable.Columns.Add("city", typeof(string));
		for (int i = 0; i < 100000; i++)
		{
			var row = MyDataTable.NewRow();
			row["id"] = i + 1;
			row["city"] = RandomCityName();
			MyDataTable.Rows.Add(row);
		}
		RefreshDataTable();
	}

	/// <summary>
	/// Unsafe parallel population: perform inserts in parallel on multiple threads without synchronisation, which can lead to exceptions and application crashes due to concurrent modifications of the DataTable.
	/// </summary>
	/// <returns>A task representing the asynchronous operation.</returns>
	[RelayCommand]
	public async Task PopulateDataTable2()
	{
		try
		{
			MyDataTable = new DataTable();
			MyDataTable.Columns.Add("id", typeof(int));
			MyDataTable.Columns.Add("city", typeof(string));
			var task1 = Task.Run(() =>
			{
				for (int i = 0; i < 50000; i++)
				{
					var row = MyDataTable.NewRow();
					row["id"] = i + 1;
					row["city"] = RandomCityName();
					MyDataTable.Rows.Add(row);
				}
			});
			var task2 = Task.Run(() =>
			{
				for (int i = 50000; i < 100000; i++)
				{
					var row = MyDataTable.NewRow();
					row["id"] = i + 1;
					row["city"] = RandomCityName();
					MyDataTable.Rows.Add(row);
				}
			});
			await Task.WhenAll(task1, task2);
			RefreshDataTable();
		}
		catch (Exception ex)
		{
			Logger?.LogCritical(ex, "Error while populating DataTable: {Message}", ex.Message);
		}
	}

	/// <summary>
	/// Marshalled population: perform inserts in parallel on multiple threads but marshal all modifications to the DataTable back on a dedicated single-threaded dispatcher to ensure thread safety and maintain a responsive UI, while avoiding exceptions and application crashes due to concurrent modifications of the DataTable.
	/// </summary>
	/// <returns>A task representing the asynchronous operation.</returns>
	[RelayCommand]
	public async Task PopulateDataTable3()
	{
		try
		{
			MainViewModel.Dispatcher.Dispatch(() =>
			{
				MyDataTable = new DataTable();
				MyDataTable.Columns.Add("id", typeof(int));
				MyDataTable.Columns.Add("city", typeof(string));
			});
			var task1 = Task.Run(() =>
			{
				for (int i = 0; i < 50000; i++)
				{
					int _i = i; // Capture loop variable for use in lambda
					MainViewModel.Dispatcher.Dispatch(() =>
					{
						var row = MyDataTable.NewRow();
						row["id"] = _i + 1;
						row["city"] = RandomCityName();
						MyDataTable.Rows.Add(row);
					});
				}
			});
			var task2 = Task.Run(async () =>
			{
				for (int i = 50000; i < 100000; i++)
				{
					int _i = i; // Capture loop variable for use in lambda
					await MainViewModel.Dispatcher.DispatchAsync(() =>
					{
						var row = MyDataTable.NewRow();
						row["id"] = _i + 1;
						row["city"] = RandomCityName();
						MyDataTable.Rows.Add(row);
					});
				}
			});
			await Task.WhenAll(task1, task2);
			MainViewModel.Dispatcher.Dispatch(() => RefreshDataTable());
		}
		catch (Exception ex)
		{
			Logger?.LogCritical(ex, "Error while populating DataTable: {Message}", ex.Message);
		}
	}

	public void RefreshDataTable()
	{
		var cities = new List<CityInfo>();
		foreach (DataRow row in MyDataTable.Rows)
		{
			cities.Add(new CityInfo() { Id = (int)row["id"], City = (string)row["city"] });
		}
		Cities = cities;
	}
}
