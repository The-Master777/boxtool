using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace DSLStatus {

    /// <summary>
    /// Interaktionslogik für DetailWindow.xaml
    /// </summary>
    public partial class DetailWindow : Window {
        readonly ObservableCollection<DetailsDataRow> _detailCollection = new ObservableCollection<DetailsDataRow>();

        public DetailWindow() {
            InitializeComponent();
        }

        public ObservableCollection<DetailsDataRow> DetailCollection { get { return _detailCollection; } }
    }

    public class DetailsDataRow {
        public string Description { get; set; }
        public string Downstream { get; set; }
        public string Upstream { get; set; }

        public DetailsDataRow(String description, String downstream, String upstream = "") {
            Description = description;
            Downstream = downstream;
            Upstream = upstream;
        }
    }
}
