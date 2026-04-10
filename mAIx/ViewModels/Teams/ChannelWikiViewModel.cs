using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using mAIx.Services.Graph;

namespace mAIx.ViewModels.Teams
{
    public class WikiSection : ObservableObject
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string AuthorName { get; set; } = "";
        public DateTime LastModified { get; set; }
        public string LastModifiedText => LastModified.ToString("yyyy-MM-dd HH:mm");
    }

    public partial class ChannelWikiViewModel : ObservableObject
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private readonly GraphTeamsService _teamsService;

        [ObservableProperty] private string _channelId = "";
        [ObservableProperty] private string _teamId = "";
        [ObservableProperty] private ObservableCollection<WikiSection> _sections = new();
        [ObservableProperty] private WikiSection? _selectedSection;
        [ObservableProperty] private bool _isEditing;
        [ObservableProperty] private string _editContent = "";
        [ObservableProperty] private bool _isLoading;

        public ChannelWikiViewModel(GraphTeamsService teamsService)
        {
            _teamsService = teamsService;
        }

        public async Task InitializeAsync(string teamId, string channelId)
        {
            TeamId = teamId;
            ChannelId = channelId;
            await LoadWikiAsync();
        }

        [RelayCommand]
        private void SelectSection(WikiSection? section)
        {
            SelectedSection = section;
            EditContent = section?.Content ?? "";
            IsEditing = false;
        }

        [RelayCommand]
        private void StartEdit()
        {
            if (SelectedSection == null) return;
            EditContent = SelectedSection.Content;
            IsEditing = true;
        }

        [RelayCommand]
        private async Task SaveEdit()
        {
            if (SelectedSection == null) return;
            SelectedSection.Content = EditContent;
            SelectedSection.LastModified = DateTime.Now;
            IsEditing = false;
            _log.Info("Wiki 섹션 저장: {Title}", SelectedSection.Title);
            await Task.CompletedTask;
        }

        [RelayCommand]
        private void CancelEdit()
        {
            IsEditing = false;
            EditContent = SelectedSection?.Content ?? "";
        }

        [RelayCommand]
        private void AddSection()
        {
            var section = new WikiSection
            {
                Id = Guid.NewGuid().ToString(),
                Title = "새 섹션",
                Content = "",
                AuthorName = "나",
                LastModified = DateTime.Now
            };
            Sections.Add(section);
            SelectSectionCommand.Execute(section);
            StartEditCommand.Execute(null);
        }

        private async Task LoadWikiAsync()
        {
            IsLoading = true;
            try
            {
                Sections.Clear();
                Sections.Add(new WikiSection
                {
                    Id = "intro",
                    Title = "소개",
                    Content = "이 채널의 Wiki 페이지입니다.\n\n여기에 팀의 중요 정보를 기록하세요.",
                    AuthorName = "팀",
                    LastModified = DateTime.Now
                });
                if (Sections.Count > 0)
                    SelectSectionCommand.Execute(Sections[0]);
                _log.Info("Wiki 로드 완료: channelId={ChannelId}", ChannelId);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Wiki 로드 실패");
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
