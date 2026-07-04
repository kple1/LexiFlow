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
        private readonly SessionService _session;
        public WordsViewModel(ApiService api, SessionService session)
        {
            _api = api;
            _session = session;
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

                // Overlay this user's progress as a status badge on each word.
                var statusByWord = await LoadProgressMapAsync();

                foreach (var a in result)
                {
                    if (statusByWord.TryGetValue(a.Id, out var status))
                        a.UserStatus = status;
                    Words.Add(a);
                }
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Error", ex.Message, "Confirm");
            }
        }

        // Returns wordId -> status for the signed-in user; empty if logged out or the
        // progress endpoint is unavailable (e.g. server not yet redeployed).
        private async Task<Dictionary<string, string>> LoadProgressMapAsync()
        {
            if (!_session.IsLoggedIn)
                return [];

            try
            {
                var progress = await _api.GetProgressAsync(_session.CurrentUserId!);
                return progress
                    .GroupBy(p => p.WordId)
                    .ToDictionary(g => g.Key, g => g.First().Status);
            }
            catch
            {
                return [];
            }
        }

        [RelayCommand]
        private async void WordInformation(string example)
        {
            await Shell.Current.DisplayAlert("", example, "Confirm");
        }
    }
}
