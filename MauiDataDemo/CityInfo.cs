// CityInfo.cs

using SQLite;

namespace MauiDataDemo;


[Table("Cities")]
public partial class CityInfo
{
	//[PrimaryKey]
	[Unique]
	public int Id { get; set; } = 0;

	//[Indexed]
	public string City { get; set; } = string.Empty;
}
