using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RttyDecoder.Wpf.Help;

/// <summary>Ajutor: subiectele in stanga (cu cautare), textul in dreapta. Fereastra nemodala.</summary>
public partial class HelpWindow : Window
{
    public HelpWindow(string? topicId = null)
    {
        InitializeComponent();
        TopicList.ItemsSource = HelpTopics.All;
        TopicList.SelectedItem = (topicId != null ? HelpTopics.Find(topicId) : null) ?? HelpTopics.All.FirstOrDefault();
        Loaded += (_, _) => SearchBox.Focus();
    }

    /// <summary>Arata subiectul cu Id-ul dat (de ex. "09-fisiere"); cautarea se goleste.</summary>
    public void ShowTopic(string id)
    {
        if (HelpTopics.Find(id) is not { } topic) return;
        SearchBox.Text = "";
        TopicList.SelectedItem = topic;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var selected = TopicList.SelectedItem as HelpTopic;
        var found = HelpTopics.Search(SearchBox.Text);
        TopicList.ItemsSource = found;
        NoResults.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TopicList.SelectedItem = selected != null && found.Contains(selected) ? selected : found.FirstOrDefault();
    }

    private void OnTopicSelected(object sender, SelectionChangedEventArgs e)
    {
        if (TopicList.SelectedItem is not HelpTopic topic) return;
        Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
        Viewer.Document = HelpDocument.Build(topic.Markdown,
            Res("Rtty.Foreground", Brushes.Gainsboro), Res("Rtty.Accent", Brushes.Cyan),
            Res("Rtty.Border", Brushes.Gray), Res("Rtty.Background", Brushes.Black));
    }
}
