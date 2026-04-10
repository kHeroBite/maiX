using System.Windows.Controls;
using NLog;

namespace mAIx.Controls.Teams;

/// <summary>
/// 채널 설정 탭 UserControl — DataContext(ChannelSettingsViewModel) 바인딩
/// </summary>
public partial class ChannelSettingsControl : UserControl
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    public ChannelSettingsControl()
    {
        InitializeComponent();
    }
}
