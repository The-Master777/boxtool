using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WpfApplication1 {

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
