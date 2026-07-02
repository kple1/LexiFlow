using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class DailyWords : ContentPage
{
    private readonly ApiService _api;

    public DailyWords(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            WordsView.ItemsSource = await _api.GetWordsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.ToString(), "Confirm");
        }
    }
}