using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace BellatorFanControl
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly BellatorWmi wmi = new BellatorWmi();
        private readonly Timer timer = new Timer();
        private readonly DataGridView grid = new DataGridView();
        private readonly Chart chart = new Chart();
        private readonly Label status = new Label();
        private readonly Label tempLabel = new Label();
        private readonly Label fanLabel = new Label();
        private readonly CheckBox autoApply = new CheckBox();
        private readonly NumericUpDown interval = new NumericUpDown();
        private readonly NumericUpDown hysteresis = new NumericUpDown();
        private readonly string configPath;
        private readonly Color appBack = Color.FromArgb(239, 239, 239);
        private readonly Color panelBack = Color.FromArgb(245, 245, 245);
        private readonly Color accent = Color.FromArgb(43, 164, 226);

        private bool draggingPoint;
        private int draggingSeriesIndex = -1;
        private DataGridViewRow draggingRow;
        private int? lastHotspot;
        private int? lastCpuGpuTarget;
        private int? lastSysTarget;

        public MainForm()
        {
            Text = "斗战者风扇控制器";
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(980, 660);
            Size = new Size(1120, 720);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = appBack;

            configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BellatorFanControl",
                "curve.ini");

            BuildUi();
            LoadCurve();
            RefreshChart();

            timer.Interval = 3000;
            timer.Tick += delegate { PollAndApply(); };
            timer.Start();
            PollAndApply();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var area = Screen.FromControl(this).WorkingArea;
            Location = new Point(
                Math.Max(area.Left, area.Right - Width - 12),
                Math.Max(area.Top, area.Bottom - Height - 12));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (autoApply.Checked)
            {
                try
                {
                    wmi.SetMaxFanSwitch(0, false);
                    wmi.SetMaxFanSwitch(1, false);
                }
                catch
                {
                }
            }
            base.OnFormClosing(e);
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 1;
            root.BackColor = appBack;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var left = new FlowLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.FlowDirection = FlowDirection.TopDown;
            left.WrapContents = false;
            left.Padding = new Padding(14);
            left.BackColor = panelBack;
            root.Controls.Add(left, 0, 0);

            AddHeader(left, "性能模式");
            AddModeButton(left, "静音模式", 2);
            AddModeButton(left, "平衡模式", 0);
            AddModeButton(left, "增强模式", 1);
            AddModeButton(left, "疯狂模式", 3);
            AddFanPowerButton(left);

            AddHeader(left, "自动控制");
            autoApply.Text = "应用自定义风扇曲线";
            autoApply.Width = 230;
            autoApply.Height = 28;
            autoApply.CheckedChanged += delegate
            {
                if (!autoApply.Checked)
                {
                    wmi.SetMaxFanSwitch(0, false);
                    wmi.SetMaxFanSwitch(1, false);
                    lastHotspot = null;
                    lastCpuGpuTarget = null;
                    lastSysTarget = null;
                    status.Text = "已关闭自定义曲线，交还固件控制";
                }
            };
            left.Controls.Add(autoApply);

            left.Controls.Add(MakeNumberRow("检查间隔 秒", interval, 2, 60, 5));
            left.Controls.Add(MakeNumberRow("降档回差 °C", hysteresis, 1, 15, 3));

            var once = new Button();
            once.Text = "立即按曲线写入一次";
            once.Width = 230;
            once.Height = 36;
            StyleButton(once, false);
            once.Click += delegate { PollAndApply(true); };
            left.Controls.Add(once);

            var restore = new Button();
            restore.Text = "恢复固件风扇控制";
            restore.Width = 230;
            restore.Height = 36;
            StyleButton(restore, false);
            restore.Click += delegate
            {
                autoApply.Checked = false;
                wmi.SetMaxFanSwitch(0, false);
                wmi.SetMaxFanSwitch(1, false);
                status.Text = "已恢复固件风扇控制";
            };
            left.Controls.Add(restore);

            AddHeader(left, "状态");
            tempLabel.Width = 240;
            tempLabel.Height = 44;
            fanLabel.Width = 240;
            fanLabel.Height = 34;
            left.Controls.Add(tempLabel);

            var right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.Padding = new Padding(12);
            right.BackColor = appBack;
            right.RowCount = 4;
            right.ColumnCount = 1;
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 72));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            root.Controls.Add(right, 1, 0);

            fanLabel.Dock = DockStyle.Fill;
            fanLabel.BackColor = Color.White;
            fanLabel.BorderStyle = BorderStyle.None;
            fanLabel.TextAlign = ContentAlignment.MiddleLeft;
            fanLabel.Padding = new Padding(12, 0, 0, 0);
            fanLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            right.Controls.Add(fanLabel, 0, 0);

            ConfigureChart();
            right.Controls.Add(chart, 0, 1);

            ConfigureGrid();
            right.Controls.Add(grid, 0, 2);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            right.Controls.Add(buttons, 0, 3);

            AddActionButton(buttons, "保存曲线", delegate
            {
                SaveCurve();
                RefreshChart();
            });
            AddActionButton(buttons, "恢复默认", delegate
            {
                LoadDefaultCurve();
                RefreshChart();
                SaveCurve();
            });
            AddActionButton(buttons, "添加点", delegate
            {
                grid.Rows.Add(70, 35, 64);
                RefreshChart();
            });
            AddActionButton(buttons, "删除点", delegate
            {
                foreach (DataGridViewRow row in grid.SelectedRows)
                {
                    if (!row.IsNewRow)
                    {
                        grid.Rows.Remove(row);
                    }
                }
                RefreshChart();
            });
        }

        private static void AddHeader(Control parent, string text)
        {
            var label = new Label();
            label.Text = text;
            label.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            label.Width = 240;
            label.Height = 30;
            label.Margin = new Padding(0, 18, 0, 4);
            parent.Controls.Add(label);
        }

        private void AddModeButton(Control parent, string text, byte mode)
        {
            var button = new Button();
            button.Text = text;
            button.Width = 230;
            button.Height = 42;
            button.Margin = new Padding(0, 4, 0, 4);
            StyleButton(button, true);
            button.Click += delegate
            {
                wmi.SetSystemMode(mode);
                status.Text = "已切换：" + text;
            };
            parent.Controls.Add(button);
        }

        private void AddFanPowerButton(Control parent)
        {
            var button = new Button();
            button.Text = "风扇 + 功率";
            button.Width = 230;
            button.Height = 42;
            button.Margin = new Padding(0, 4, 0, 4);
            StyleButton(button, true);
            button.Click += delegate
            {
                autoApply.Checked = true;
                PollAndApply(true);
                status.Text = "已启用自定义风扇曲线，并按当前温度写入一次。";
            };
            parent.Controls.Add(button);
        }

        private static Control MakeNumberRow(string text, NumericUpDown input, int min, int max, int value)
        {
            var panel = new Panel();
            panel.Width = 240;
            panel.Height = 34;

            var label = new Label();
            label.Text = text;
            label.Location = new Point(0, 8);
            label.Width = 130;
            panel.Controls.Add(label);

            input.Minimum = min;
            input.Maximum = max;
            input.Value = value;
            input.Width = 80;
            input.Location = new Point(145, 4);
            panel.Controls.Add(input);
            return panel;
        }

        private static void AddActionButton(Control parent, string text, EventHandler handler)
        {
            var button = new Button();
            button.Text = text;
            button.Width = 120;
            button.Height = 34;
            button.Margin = new Padding(8, 10, 0, 0);
            StyleButton(button, false);
            button.Click += handler;
            parent.Controls.Add(button);
        }

        private static void StyleButton(Button button, bool large)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = large ? Color.FromArgb(210, 210, 210) : Color.FromArgb(220, 220, 220);
            button.FlatAppearance.MouseOverBackColor = Color.White;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(226, 244, 252);
            button.BackColor = Color.FromArgb(250, 250, 250);
            button.ForeColor = Color.Black;
            button.Cursor = Cursors.Hand;
        }

        private void ConfigureGrid()
        {
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;
            grid.DefaultCellStyle.SelectionBackColor = accent;
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.Columns.Add("Temp", "温度 °C");
            grid.Columns.Add("CpuGpu", "大风扇 x100RPM");
            grid.Columns.Add("Sys", "小风扇 x100RPM");
            grid.CellEndEdit += delegate { RefreshChart(); };
            grid.RowsRemoved += delegate { RefreshChart(); };
            grid.RowsAdded += delegate { RefreshChart(); };
        }

        private void ConfigureChart()
        {
            chart.Dock = DockStyle.Fill;
            chart.BackColor = appBack;
            chart.ChartAreas.Add("main");
            var area = chart.ChartAreas["main"];
            area.BackColor = Color.White;
            area.AxisX.Minimum = 20;
            area.AxisX.Maximum = 105;
            area.AxisX.Interval = 5;
            area.AxisX.Title = "温度 °C";
            area.AxisY.Minimum = 0;
            area.AxisY.Maximum = 8400;
            area.AxisY.Interval = 400;
            area.AxisY.Title = "RPM";
            area.AxisX.MajorGrid.LineColor = Color.Gainsboro;
            area.AxisY.MajorGrid.LineColor = Color.Gainsboro;

            AddSeries("大风扇", Color.FromArgb(43, 164, 226));
            AddSeries("小风扇", Color.FromArgb(120, 120, 120));

            chart.MouseDown += Chart_MouseDown;
            chart.MouseMove += Chart_MouseMove;
            chart.MouseUp += Chart_MouseUp;
        }

        private void AddSeries(string name, Color color)
        {
            var series = new Series(name);
            series.ChartType = SeriesChartType.Line;
            series.BorderWidth = 3;
            series.MarkerStyle = MarkerStyle.Circle;
            series.MarkerSize = 8;
            series.Color = color;
            chart.Series.Add(series);
        }

        private void LoadCurve()
        {
            if (!File.Exists(configPath))
            {
                LoadDefaultCurve();
                return;
            }

            grid.Rows.Clear();
            foreach (var line in File.ReadAllLines(configPath))
            {
                var parts = line.Split(',');
                int temp;
                int cpuGpu;
                int sys;
                if (parts.Length == 3 &&
                    int.TryParse(parts[0], out temp) &&
                    int.TryParse(parts[1], out cpuGpu) &&
                    int.TryParse(parts[2], out sys))
                {
                    grid.Rows.Add(temp, cpuGpu, sys);
                }
            }

            if (grid.Rows.Count == 0)
            {
                LoadDefaultCurve();
            }
        }

        private void LoadDefaultCurve()
        {
            grid.Rows.Clear();
            grid.Rows.Add(50, 22, 20);
            grid.Rows.Add(55, 26, 35);
            grid.Rows.Add(60, 29, 48);
            grid.Rows.Add(65, 32, 59);
            grid.Rows.Add(70, 35, 64);
            grid.Rows.Add(75, 38, 69);
            grid.Rows.Add(80, 40, 75);
            grid.Rows.Add(85, 43, 80);
        }

        private void SaveCurve()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            File.WriteAllLines(configPath, ReadCurve().Select(p => p.Temp + "," + p.CpuGpu + "," + p.Sys).ToArray());
            status.Text = "曲线已保存：" + configPath;
        }

        private List<CurvePoint> ReadCurve()
        {
            var points = new List<CurvePoint>();
            foreach (var point in ReadCurveRows())
            {
                points.Add(new CurvePoint(point.Temp, point.CpuGpu, point.Sys));
            }
            return points;
        }

        private List<CurveRowPoint> ReadCurveRows()
        {
            var points = new List<CurveRowPoint>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                int temp;
                int cpuGpu;
                int sys;
                if (TryCell(row, 0, out temp) && TryCell(row, 1, out cpuGpu) && TryCell(row, 2, out sys))
                {
                    cpuGpu = Math.Max(0, Math.Min(44, cpuGpu));
                    sys = Math.Max(0, Math.Min(82, sys));
                    points.Add(new CurveRowPoint(row, temp, cpuGpu, sys));
                }
            }
            return points.OrderBy(p => p.Temp).ToList();
        }

        private static bool TryCell(DataGridViewRow row, int index, out int value)
        {
            value = 0;
            if (row.Cells[index].Value == null)
            {
                return false;
            }
            return int.TryParse(Convert.ToString(row.Cells[index].Value, CultureInfo.InvariantCulture), out value);
        }

        private void RefreshChart()
        {
            if (chart.Series.Count == 0)
            {
                return;
            }

            chart.Series[0].Points.Clear();
            chart.Series[1].Points.Clear();
            foreach (var point in ReadCurveRows())
            {
                int cpuPoint = chart.Series[0].Points.AddXY(point.Temp, point.CpuGpu * 100);
                chart.Series[0].Points[cpuPoint].Tag = point.Row;

                int sysPoint = chart.Series[1].Points.AddXY(point.Temp, point.Sys * 100);
                chart.Series[1].Points[sysPoint].Tag = point.Row;
            }
        }

        private void Chart_MouseDown(object sender, MouseEventArgs e)
        {
            var hit = chart.HitTest(e.X, e.Y);
            if (hit.ChartElementType != ChartElementType.DataPoint || hit.Series == null)
            {
                return;
            }

            draggingSeriesIndex = chart.Series.IndexOf(hit.Series);
            if (draggingSeriesIndex < 0 || draggingSeriesIndex > 1)
            {
                return;
            }

            draggingRow = hit.Series.Points[hit.PointIndex].Tag as DataGridViewRow;
            if (draggingRow == null)
            {
                return;
            }

            draggingPoint = true;
            chart.Cursor = Cursors.Hand;
            grid.ClearSelection();
            draggingRow.Selected = true;
            status.Text = "拖动曲线点调整温度和转速，松开鼠标后可保存曲线。";
        }

        private void Chart_MouseMove(object sender, MouseEventArgs e)
        {
            if (!draggingPoint || draggingRow == null)
            {
                var hit = chart.HitTest(e.X, e.Y);
                chart.Cursor = hit.ChartElementType == ChartElementType.DataPoint ? Cursors.Hand : Cursors.Default;
                return;
            }

            var area = chart.ChartAreas["main"];
            double rawTemp;
            double rawRpm;
            try
            {
                rawTemp = area.AxisX.PixelPositionToValue(e.X);
                rawRpm = area.AxisY.PixelPositionToValue(e.Y);
            }
            catch
            {
                return;
            }

            int temp = Snap(Clamp((int)Math.Round(rawTemp), 20, 105), 5);
            int maxFan = draggingSeriesIndex == 0 ? 44 : 82;
            int fanUnits = Clamp(Snap((int)Math.Round(rawRpm), 100) / 100, 0, maxFan);

            draggingRow.Cells[0].Value = temp;
            draggingRow.Cells[draggingSeriesIndex == 0 ? 1 : 2].Value = fanUnits;
            RefreshChart();

            status.Text = (draggingSeriesIndex == 0 ? "大风扇" : "小风扇") +
                          " 曲线点：" + temp + " °C / " + (fanUnits * 100) + " RPM";
        }

        private void Chart_MouseUp(object sender, MouseEventArgs e)
        {
            if (!draggingPoint)
            {
                return;
            }

            draggingPoint = false;
            draggingSeriesIndex = -1;
            draggingRow = null;
            chart.Cursor = Cursors.Default;
            RefreshChart();
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static int Snap(int value, int step)
        {
            return (int)(Math.Round(value / (double)step) * step);
        }

        private void PollAndApply()
        {
            PollAndApply(false);
        }

        private void PollAndApply(bool force)
        {
            timer.Interval = Math.Max(1, (int)interval.Value) * 1000;

            try
            {
                int? cpuTemp = wmi.GetCpuTemperature();
                int? gpuTemp = NvidiaSmi.GetGpuTemperature();
                var speeds = wmi.GetFanSpeeds();
                int hotspot = Math.Max(cpuTemp.GetValueOrDefault(0), gpuTemp.GetValueOrDefault(0));

                tempLabel.Text = "CPU 温度：" + FormatTemp(cpuTemp) + Environment.NewLine +
                                 "GPU 温度：" + FormatTemp(gpuTemp);
                fanLabel.Text = "大风扇 1  " + FormatRpm(speeds.CpuGpu) +
                                "      大风扇 2  " + FormatRpm(speeds.Gpu) +
                                "      小风扇  " + FormatRpm(speeds.Sys);

                if (hotspot <= 0)
                {
                    status.Text = "没有读到有效温度，未写入风扇。";
                    return;
                }

                var target = PickTarget(hotspot);
                bool shouldWrite = force || ShouldWrite(hotspot, target);
                if (autoApply.Checked && shouldWrite)
                {
                    wmi.SetMaxFanSwitch(0, true);
                    wmi.SetMaxFanSpeed(0, (byte)target.CpuGpu);
                    wmi.SetMaxFanSwitch(1, true);
                    wmi.SetMaxFanSpeed(1, (byte)target.Sys);
                    lastHotspot = hotspot;
                    lastCpuGpuTarget = target.CpuGpu;
                    lastSysTarget = target.Sys;
                    status.Text = "已写入曲线：大风扇 " + (target.CpuGpu * 100) +
                                  " RPM，小风扇 " + (target.Sys * 100) + " RPM";
                }
                else
                {
                    status.Text = "目标：大风扇 " + (target.CpuGpu * 100) +
                                  " RPM，小风扇 " + (target.Sys * 100) +
                                  " RPM。" + (autoApply.Checked ? "等待回差/档位变化。" : "未启用写入。");
                }
            }
            catch (Exception ex)
            {
                status.Text = "错误：" + ex.Message;
            }
        }

        private bool ShouldWrite(int hotspot, CurvePoint target)
        {
            if (!lastHotspot.HasValue)
            {
                return true;
            }
            if (hotspot > lastHotspot.Value)
            {
                return target.CpuGpu != lastCpuGpuTarget || target.Sys != lastSysTarget;
            }
            if ((lastHotspot.Value - hotspot) >= (int)hysteresis.Value)
            {
                return target.CpuGpu != lastCpuGpuTarget || target.Sys != lastSysTarget;
            }
            return false;
        }

        private CurvePoint PickTarget(int temp)
        {
            var points = ReadCurve();
            if (points.Count == 0)
            {
                LoadDefaultCurve();
                points = ReadCurve();
            }

            CurvePoint target = points[0];
            foreach (var point in points)
            {
                if (temp >= point.Temp)
                {
                    target = point;
                }
            }
            return target;
        }

        private static string FormatTemp(int? temp)
        {
            return temp.HasValue && temp.Value > 0 ? temp.Value + " °C" : "N/A";
        }

        private static string FormatRpm(int rpm)
        {
            return rpm >= 0 ? rpm + " RPM" : "N/A";
        }
    }

    internal class CurvePoint
    {
        public readonly int Temp;
        public readonly int CpuGpu;
        public readonly int Sys;

        public CurvePoint(int temp, int cpuGpu, int sys)
        {
            Temp = temp;
            CpuGpu = cpuGpu;
            Sys = sys;
        }
    }

    internal sealed class CurveRowPoint : CurvePoint
    {
        public readonly DataGridViewRow Row;

        public CurveRowPoint(DataGridViewRow row, int temp, int cpuGpu, int sys)
            : base(temp, cpuGpu, sys)
        {
            Row = row;
        }
    }

    internal sealed class FanSpeeds
    {
        public int CpuGpu = -1;
        public int Gpu = -1;
        public int Sys = -1;
    }

    internal sealed class BellatorWmi
    {
        private const byte MethodGet = 250;
        private const byte MethodSet = 251;

        public int? GetCpuTemperature()
        {
            var output = Invoke(Make(MethodGet, 22));
            if (output == null || output.Length < 5 || output[4] == 0 || output[4] == 255)
            {
                return null;
            }
            return output[4];
        }

        public FanSpeeds GetFanSpeeds()
        {
            var result = new FanSpeeds();
            var output = Invoke(Make(MethodGet, 13));
            if (output == null || output.Length < 12)
            {
                return result;
            }
            result.CpuGpu = (output[5] << 8) + output[4];
            result.Gpu = (output[7] << 8) + output[6];
            result.Sys = (output[11] << 8) + output[10];
            return result;
        }

        public void SetSystemMode(byte mode)
        {
            var data = Make(MethodSet, 8);
            data[4] = mode;
            Invoke(data);
        }

        public void SetMaxFanSwitch(byte fanType, bool on)
        {
            var data = Make(MethodSet, 20);
            data[4] = fanType;
            data[5] = on ? (byte)1 : (byte)0;
            Invoke(data);
        }

        public void SetMaxFanSpeed(byte fanType, byte speed)
        {
            var data = Make(MethodSet, 21);
            data[4] = fanType;
            data[5] = speed;
            Invoke(data);
        }

        private static byte[] Make(byte methodType, byte methodName)
        {
            var data = new byte[32];
            data[1] = methodType;
            data[3] = methodName;
            return data;
        }

        private static byte[] Invoke(byte[] input)
        {
            var obj = new ManagementObject(
                "root\\WMI",
                "MICommonInterface.InstanceName='ACPI\\PNP0C14\\MIFS_0'",
                null);
            var parameters = obj.GetMethodParameters("MiInterface");
            parameters["InData"] = input;
            var output = obj.InvokeMethod("MiInterface", parameters, null);
            return output["OutData"] as byte[];
        }
    }

    internal static class NvidiaSmi
    {
        public static int? GetGpuTemperature()
        {
            foreach (var path in CandidatePaths())
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var process = new Process();
                    process.StartInfo.FileName = path;
                    process.StartInfo.Arguments = "--query-gpu=temperature.gpu --format=csv,noheader,nounits";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(1500);

                    int max = 0;
                    foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int value;
                        if (int.TryParse(line.Trim(), out value))
                        {
                            max = Math.Max(max, value);
                        }
                    }
                    if (max > 0)
                    {
                        return max;
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        private static IEnumerable<string> CandidatePaths()
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
            yield return "nvidia-smi.exe";
        }
    }
}
