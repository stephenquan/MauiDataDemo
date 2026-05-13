// MainPage.xaml.cs

namespace MauiDataDemo;

public partial class MainPage : ContentPage
{
	public MainViewModel VM { get; } = new();

	public MainPage()
	{
		BindingContext = this.VM;
		InitializeComponent();
	}
}
