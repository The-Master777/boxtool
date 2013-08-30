using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using FritzBoxApi;

namespace DSLStatus
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private CancellationTokenSource _cancellationTokenSource;

        private void AnimateLoadStart() {
            GridProgress.Opacity = 0;
            LabelLoad.Text = "";

            var sb = new Storyboard();

            // Animate Height
            var aniHeight = new DoubleAnimationUsingKeyFrames();

            aniHeight.KeyFrames.Add(new EasingDoubleKeyFrame(107, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            aniHeight.KeyFrames.Add(new EasingDoubleKeyFrame(107 + GridProgress.ActualHeight + 15, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, 150)), new SineEase { EasingMode = EasingMode.EaseOut }));


            Storyboard.SetTarget(aniHeight, this);
            Storyboard.SetTargetProperty(aniHeight, new PropertyPath(HeightProperty));

            sb.Children.Add(aniHeight);

            // Animate Opacity
            var aniOpacityProgres = new DoubleAnimationUsingKeyFrames();

            aniOpacityProgres.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, 100))));
            aniOpacityProgres.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, 300)), new CubicEase { EasingMode = EasingMode.EaseIn }));

            Storyboard.SetTarget(aniOpacityProgres, GridProgress);
            Storyboard.SetTargetProperty(aniOpacityProgres, new PropertyPath(OpacityProperty));

            sb.Children.Add(aniOpacityProgres);

            var aniOpacityLogin = new DoubleAnimationUsingKeyFrames();

            aniOpacityLogin.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            aniOpacityLogin.KeyFrames.Add(new EasingDoubleKeyFrame(0.333, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, 300)), new CubicEase { EasingMode = EasingMode.EaseOut }));

            Storyboard.SetTarget(aniOpacityLogin, GridLogin);
            Storyboard.SetTargetProperty(aniOpacityLogin, new PropertyPath(OpacityProperty));

            sb.Children.Add(aniOpacityLogin);
            
            GridLogin.IsEnabled = false;
            sb.Begin();
        }

        private void AnimateLoadStop() {
            GridLogin.IsEnabled = true;

            var sb = new Storyboard();

            // Animate Height
            var aniHeight2 = new DoubleAnimationUsingKeyFrames();

            aniHeight2.KeyFrames.Add(new EasingDoubleKeyFrame(107 + GridProgress.ActualHeight + 15, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, 0))));
            aniHeight2.KeyFrames.Add(new EasingDoubleKeyFrame(107, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, 0 + 150)), new SineEase { EasingMode = EasingMode.EaseOut }));


            Storyboard.SetTarget(aniHeight2, this);
            Storyboard.SetTargetProperty(aniHeight2, new PropertyPath(HeightProperty));

            sb.Children.Add(aniHeight2);

            // Animate Opacity
            var aniOpacityProgres2 = new DoubleAnimationUsingKeyFrames();

            aniOpacityProgres2.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, 0 + 100))));
            aniOpacityProgres2.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, 0 + 300)), new CubicEase { EasingMode = EasingMode.EaseIn }));

            Storyboard.SetTarget(aniOpacityProgres2, GridProgress);
            Storyboard.SetTargetProperty(aniOpacityProgres2, new PropertyPath(OpacityProperty));

            sb.Children.Add(aniOpacityProgres2);

            var aniOpacityLogin2 = new DoubleAnimationUsingKeyFrames();

            aniOpacityLogin2.KeyFrames.Add(new EasingDoubleKeyFrame(0.333, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, 0))));
            aniOpacityLogin2.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, 0 + 300)), new CubicEase { EasingMode = EasingMode.EaseOut }));

            Storyboard.SetTarget(aniOpacityLogin2, GridLogin);
            Storyboard.SetTargetProperty(aniOpacityLogin2, new PropertyPath(OpacityProperty));

            sb.Children.Add(aniOpacityLogin2);
            sb.Begin();
        }

        private async void Button_Click(object sender, RoutedEventArgs e) {
            AnimateLoadStart();

            var progress = new MyProgressHandler(SynchronizationContext.Current);

            progress.ProgressReported += tuple => {
                LabelLoad.Text = String.Format("{0} / {1} ({2:.00}%)", tuple.Item1, tuple.Item2, tuple.Item1 * 100.0 / tuple.Item2);
                ProgressBarLoad.Maximum = tuple.Item2;
                ProgressBarLoad.Value = tuple.Item1;
            };
            
            ProgressBarLoad.Value = 0;

            if(_cancellationTokenSource == null)
                _cancellationTokenSource = new CancellationTokenSource();

            Session session;
            
            try {
                session = await FritzBox.ConnectAsync(TextBoxHost.Text, TextBoxPassword.Password, CancellationToken.None);
            } catch(OperationCanceledException) {
                return;
            } catch(Exception ex) {
                AnimateLoadStop();
                MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LabelLoad.Text = session.Id;

            var data = new ExampleQuery();

            try {
                await session.QueryAsync(data, _cancellationTokenSource.Token, progress);
            } catch(OperationCanceledException) {
                return;
            } catch(Exception ex) {
                MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            } finally {
                AnimateLoadStop();
            }

            // Use data ...
            //MessageBox.Show(this, data.DslamVendor);

            var newWindow = new DetailWindow();
            newWindow.DetailCollection.Clear();
            FillDetailCollection(newWindow.DetailCollection, data);
            newWindow.Show();
        }

        private void FillDetailCollection(ObservableCollection<DetailsDataRow> collection, ExampleQuery data) {
            // FW
            collection.Add(new DetailsDataRow("Firmware", data.Firmware));

            // DSL-Driver
            collection.Add(new DetailsDataRow("DSL-Treiber", data.DslDriver));

            // DSLAM
            collection.Add(new DetailsDataRow("DSLAM", data.DslamVendor, data.DslamVendorVersion));
            collection.Add(new DetailsDataRow("", data.DslamVersion, data.DslamSerialNumber));

            // MaxSync
            collection.Add(new DetailsDataRow( "DSLAM-Datenrate Max.", String.Format("{0:#,0} kbit/s", data.Dsl.DsMaxDslamRate), String.Format("{0:#,0} kbit/s", data.Dsl.UsMaxDslamRate) ));

            // MinSync
            collection.Add(new DetailsDataRow( "DSLAM-Datenrate Min.", String.Format("{0:#,0} kbit/s", data.Dsl.DsMinDslamRate), String.Format("{0:#,0} kbit/s", data.Dsl.UsMinDslamRate) ));

            // Capacity
            collection.Add(new DetailsDataRow( "Leitungskapazität", String.Format("{0:#,0} kbit/s", data.Dsl.DsCapacity), String.Format("{0:#,0} kbit/s", data.Dsl.UsCapacity) ));

            // Sync
            collection.Add(new DetailsDataRow( "Synchronisation", String.Format("{0:#,0} kbit/s ({1:0.00}%)", data.Dsl.DsDataRate, data.Dsl.DsDataRate * 100.0 / data.Dsl.DsCapacity), String.Format("{0:#,0} kbit/s ({1:0.00}%)", data.Dsl.UsDataRate, data.Dsl.UsDataRate * 100.0 / data.Dsl.UsCapacity) ));

            // Conversion
            collection.Add(new DetailsDataRow("", SyncToReal(data.Dsl.DsDataRate), SyncToReal(data.Dsl.UsDataRate) ));

            // SNRM
            collection.Add(new DetailsDataRow( "Störabstandsmarge (SNRM)", String.Format("{0} dB", data.Dsl.DsSnrm), String.Format("{0} dB", data.Dsl.DsSnrm)));

            // Attn
            collection.Add(new DetailsDataRow( "Leitungsdämpfung", String.Format("{0} dB", data.Dsl.DsAttenuation), String.Format("{0} dB", data.Dsl.UsAttenuation)));
            collection.Add(new DetailsDataRow( "Mittlere Dämpfung", String.Format("ca. {0:0.0} dB", (data.Dsl.DsAttenuation + data.Dsl.UsAttenuation) / 2.0), String.Empty ));

            // PCB
            collection.Add(new DetailsDataRow( "Leistungsreduzierung (PCB)", String.Format("{0} dB", data.Dsl.DsPcb), String.Format("{0} dB", data.Dsl.UsPcb)));

        }


        private const double NETTOMTU = 1452;
        private const double BRUTTOMTU = 1696;

        public static String SyncToReal(int k, double nettoMtu = NETTOMTU, double bruttoMtu = BRUTTOMTU) {
            string[] prefixes = { "Byte", "kB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB", "" };

            var c = k * 1000 * (nettoMtu / bruttoMtu) / 8;
            var p = (int)Math.Truncate(Math.Log(c, 2) / 10);

            if(p < 0 || p > prefixes.Length)
                p = 0;

            return String.Format("{0:#,0.00} {1}/s", c / ((UInt64)1 << (p * 10)), prefixes[p]);
        }

        /* Definition of Converter-Functions to use */
        public abstract class BaseQueryObject {
            [QueryValueConverter]
            protected readonly Func<String, Object> IntConverter = (s => String.IsNullOrEmpty(s) || s.Equals(@"er") ? -1 : int.Parse(s));

            [QueryValueConverter]
            protected readonly Func<String, Object> BooleanConverter = (s => !String.IsNullOrEmpty(s) && !s.Equals(@"er") && (int.Parse(s) != 0));

            [QueryValueConverter]
            protected readonly Func<String, Object> FloatConverter = (s => String.IsNullOrEmpty(s) || s.Equals(@"er") ? Single.NaN : Single.Parse(s));

            [QueryValueConverter]
            protected readonly Func<String, Object> IntArrayConverter = (s => s == null ? new int[0] : Array.ConvertAll(s.Split(','), int.Parse));
        }

        /* Declarative definition of values to query */
        public class ExampleQuery {
            [QueryParameterAttribute(@"logic:status/nspver")]
            public String Firmware { get; set; }

            [QueryParameterAttribute(@"sar:status/DSP_Datapump_ver")]
            public String DslDriver { get; set; }

            [QueryParameterAttribute(@"sar:status/ATUC_vendor_ID")]
            public String DslamVendor { get; set; }

            [QueryParameterAttribute(@"sar:status/ATUC_vendor_version")]
            public String DslamVendorVersion { get; set; }

            [QueryParameterAttribute(@"sar:status/dslam_VendorID")]
            public String DslamVendorId { get; set; }

            [QueryParameterAttribute(@"sar:status/dslam_VersionNumber")]
            public String DslamVersion { get; set; }

            [QueryParameterAttribute(@"sar:status/dslam_SerialNumber")]
            public String DslamSerialNumber { get; set; }

            [QueryPropagation]
            public BinData Bin { get; set; }

            [QueryPropagation]
            public DslData Dsl { get; set; }

            public ExampleQuery() {
                Bin = new BinData();
                Dsl = new DslData();
            }

            public class BinData : BaseQueryObject {
                [QueryParameterAttribute(@"sar:status/ds_snrArrayXML", "IntArrayConverter")]
                public int[] Snr { get; set; }

                [QueryParameterAttribute(@"sar:status/bitsArrayXML", "IntArrayConverter")]
                public int[] Bits { get; set; }

                [QueryParameterAttribute(@"sar:status/pilot", "IntConverter")]
                public int PilotTone { get; set; }

                public BinData() {
                    PilotTone = -1;

                    Snr = null;
                    Bits = null;
                }
            }

            public class DslData : BaseQueryObject {
                [QueryParameterAttribute(@"sar:status/exp_ds_max_rate", "IntConverter")]
                public int DsMaxDslamRate { get; set; }

                [QueryParameterAttribute(@"sar:status/exp_us_max_rate", "IntConverter")]
                public int UsMaxDslamRate { get; set; }

                [QueryParameterAttribute(@"sar:status/exp_ds_min_rate", "IntConverter")]
                public int DsMinDslamRate { get; set; }

                [QueryParameterAttribute(@"sar:status/exp_us_min_rate", "IntConverter")]
                public int UsMinDslamRate { get; set; }

                [QueryParameterAttribute(@"sar:status/ds_attainable", "IntConverter")]
                public int DsCapacity { get; set; }

                [QueryParameterAttribute(@"sar:status/us_attainable", "IntConverter")]
                public int UsCapacity { get; set; }

                [QueryParameterAttribute(@"sar:status/dsl_ds_rate", "IntConverter")]
                public int DsDataRate { get; set; }

                [QueryParameterAttribute(@"sar:status/dsl_us_rate", "IntConverter")]
                public int UsDataRate { get; set; }


                [QueryParameterAttribute(@"sar:status/ds_path", "BooleanConverter")]
                public Boolean DsInterleaving { get; set; }

                [QueryParameterAttribute(@"sar:status/us_path", "BooleanConverter")]
                public Boolean UsInterleaving { get; set; }

                [QueryParameterAttribute(@"sar:status/ds_delay", "IntConverter")]
                public int DsDelay { get; set; }

                [QueryParameterAttribute(@"sar:status/us_delay", "IntConverter")]
                public int UsDelay { get; set; }


                [QueryParameterAttribute(@"sar:status/exp_ds_olr_Bitswap", "BooleanConverter")]
                public Boolean DsBitswap { get; set; }

                [QueryParameterAttribute(@"sar:status/exp_us_olr_Bitswap", "BooleanConverter")]
                public Boolean UsBitswap { get; set; }

                [QueryParameterAttribute(@"sar:status/exp_ds_olr_SeamlessRA", "BooleanConverter")]
                public Boolean DsSra { get; set; }

                [QueryParameterAttribute(@"sar:status/exp_ds_olr_SeamlessRA", "BooleanConverter")]
                public Boolean UsSra { get; set; }


                [QueryParameterAttribute(@"sar:status/exp_ds_inp_act", "FloatConverter")]
                public Single DsInp { get; set; }

                [QueryParameterAttribute(@"sar:status/exp_us_inp_act", "FloatConverter")]
                public Single UsInp { get; set; }


                [QueryParameterAttribute(@"sar:status/ds_margin", "IntConverter")]
                public int DsSnrm { get; set; }

                [QueryParameterAttribute(@"sar:status/us_margin", "IntConverter")]
                public int UsSnrm { get; set; }


                [QueryParameterAttribute(@"sar:status/ds_attenuation", "IntConverter")]
                public int DsAttenuation { get; set; }

                [QueryParameterAttribute(@"sar:status/us_attenuation", "IntConverter")]
                public int UsAttenuation { get; set; }


                [QueryParameterAttribute(@"sar:status/ds_powercutback", "IntConverter")]
                public int DsPcb { get; set; }

                [QueryParameterAttribute(@"sar:status/us_powercutback", "IntConverter")]
                public int UsPcb { get; set; }


                [QueryParameterAttribute(@"sar:status/exp_ds_max_nom_atp", "FloatConverter")]
                public Single DsAtp { get; set; }

                [QueryParameterAttribute(@"sar:status/exp_us_max_nom_atp", "FloatConverter")]
                public Single UsAtp { get; set; }


                [QueryParameterAttribute(@"sar:status/dsl_tone_set")]
                public String DslToneSet { get; set; }

                [QueryParameterAttribute(@"sar:status/dsl_carrier_state", "IntConverter")]
                public int CarrierState { get; set; }

                [QueryParameterAttribute(@"sar:status/dsl_train_state", "IntConverter")]
                public int TrainState { get; set; }

                [QueryParameterAttribute(@"sar:status/trained_mode")]
                public String TrainedMode { get; set; }

                [QueryParameterAttribute(@"sar:settings/DownstreamMarginOffset", "IntConverter")]
                public int DownstreamSnrOffset { get; set; }
            }
        }

        protected class MyProgressHandler : IProgress<Tuple<int, int>> {
            private int hi = -1;

            protected readonly Object Lock = new Object();

            public event Action<Tuple<int, int>> ProgressReported;

            protected SynchronizationContext Context;

            public MyProgressHandler(SynchronizationContext context) {
                if(context == null)
                    throw new ArgumentNullException("context");

                Context = context;
            }

            public void Report(Tuple<int, int> t) {
                lock(Lock) {
                    if(hi >= t.Item1) {
                        return;
                    }

                    hi = t.Item1;
                }

                Context.Post(p => {
                    if(ProgressReported != null)
                        ProgressReported((Tuple<int, int>)p);
                }, t);
            }
        }
    }
}
