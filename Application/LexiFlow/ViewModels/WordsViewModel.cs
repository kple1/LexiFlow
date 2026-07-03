using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexiFlow.Models;
using LexiFlow.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace LexiFlow.ViewModels
{
    public partial class WordsViewModel : ObservableObject
    {
        private readonly ApiService _api;
        public WordsViewModel(ApiService api)
        {
            _api = api;
        }

        public ObservableCollection<Word> Words { get; set; } = [];

        [ObservableProperty]
        private string _searchText;

        [RelayCommand]
        private async void LoadWords()
        {
            try
            {
                Words.Clear();

                var result = await _api.GetWordsAsync();
                if (!string.IsNullOrEmpty(_searchText))
                {
                    result = result.Where(x => x.English.Contains(_searchText) || x.Meaning.Contains(_searchText)).ToList();
                }
                    
                foreach (var a in result)
                {
                    Words.Add(a);
                }
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Error", ex.Message, "Confirm");
            }
        }

        [RelayCommand]
        private async void WordInformation(string example)
        {
            await Shell.Current.DisplayAlert("", example, "Confirm");
        }
    }
}
