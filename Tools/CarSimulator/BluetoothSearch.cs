﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Bluetooth.Factory;
using InTheHand.Net.Sockets;

namespace CarSimulator
{
    public partial class BluetoothSearch : Form
    {
        private readonly BluetoothClient _cli;
        private IBluetoothClient _icli;
        private readonly List<BluetoothDeviceInfo> _deviceList;
        private BluetoothComponent _bco;
        private volatile bool _searching;
        private ListViewItem _selectedItem;
        private bool _ignoreSelection;

        public BluetoothSearch()
        {
            InitializeComponent();
            ActiveControl = buttonOk;
            try
            {
                _cli = new BluetoothClient();
                FieldInfo impField = typeof(BluetoothClient).GetField("m_impl", BindingFlags.NonPublic | BindingFlags.Instance);
                if (impField != null)
                {
                    _icli = impField.GetValue(_cli) as IBluetoothClient;
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText(ex.Message);
            }
            _deviceList = new List<BluetoothDeviceInfo>();
            UpdateButtonStatus();
        }

        private bool StartDeviceSearch()
        {
            UpdateDeviceList(null, true);
            if (_cli == null)
            {
                return false;
            }

            if (_bco != null)
            {
                return false;
            }

            try
            {
                _deviceList.Clear();

                if (_icli == null)
                {
                    _bco = new BluetoothComponent(_cli);
                    _bco.DiscoverDevicesProgress += (sender, args) =>
                    {
                        if (args.Error == null && !args.Cancelled && args.Devices != null)
                        {
                            try
                            {
                                foreach (BluetoothDeviceInfo device in args.Devices)
                                {
                                    device.Refresh();
                                }
                            }
                            catch (Exception)
                            {
                                // ignored
                            }
                            BeginInvoke((Action)(() =>
                            {
                                UpdateDeviceList(args.Devices, false);
                            }));
                        }
                    };

                    _bco.DiscoverDevicesComplete += (sender, args) =>
                    {
                        _bco?.Dispose();
                        _bco = null;
                        _searching = false;
                        UpdateButtonStatus();

                        BeginInvoke((Action)(() =>
                        {
                            if (args.Error == null && !args.Cancelled)
                            {
                                UpdateDeviceList(args.Devices, true);
                                UpdateStatusText(listViewDevices.Items.Count > 0 ? "Devices found" : "No devices found");
                            }
                            else
                            {
                                // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
                                if (args.Error != null)
                                {
                                    UpdateStatusText(string.Format("Searching failed: {0}", args.Error.Message));
                                }
                                else
                                {
                                    UpdateStatusText("Searching failed");
                                }
                            }
                        }));
                    };
                    _bco.DiscoverDevicesAsync(255, true, false, true, IsWinVistaOrHigher(), _bco);
                }
                else
                {
                    IAsyncResult asyncResult = _icli.BeginDiscoverDevices(255, true, false, true, IsWinVistaOrHigher(), ar =>
                    {
                        if (ar.IsCompleted)
                        {
                            _searching = false;
                            UpdateButtonStatus();

                            BluetoothDeviceInfo[] devices = _cli.EndDiscoverDevices(ar);

                            BeginInvoke((Action)(() =>
                            {
                                UpdateDeviceList(devices, true);
                                UpdateStatusText(listViewDevices.Items.Count > 0 ? "Devices found" : "No devices found");
                            }));
                        }
                    }, this, (p1, p2) =>
                    {
                        BluetoothDeviceInfo deviceInfo = new BluetoothDeviceInfo(p1.DeviceAddress);
                        try
                        {
                            deviceInfo.Refresh();
                        }
                        catch (Exception)
                        {
                            // ignored
                        }

                        BeginInvoke((Action)(() =>
                        {
                            UpdateDeviceList(new[] { deviceInfo }, false);
                        }));

                    }, this);
                }

                _searching = true;
                UpdateStatusText("Searching ...");
                UpdateButtonStatus();
            }
            catch (Exception)
            {
                UpdateStatusText("Searching failed");
                return false;
            }
            return true;
        }

        private void UpdateDeviceList(BluetoothDeviceInfo[] devices, bool completed)
        {
            _ignoreSelection = true;
            listViewDevices.BeginUpdate();
            listViewDevices.Items.Clear();
            if (devices != null)
            {
                if (completed)
                {
                    _deviceList.Clear();
                    _deviceList.AddRange(devices);
                }
                else
                {
                    foreach (BluetoothDeviceInfo device in devices.OrderBy(dev => dev.DeviceAddress.ToString()))
                    {
                        for (int i = 0; i < _deviceList.Count; i++)
                        {
                            if (_deviceList[i].DeviceAddress == device.DeviceAddress)
                            {
                                _deviceList.RemoveAt(i);
                                i--;
                            }
                        }
                        _deviceList.Add(device);
                    }
                }

                foreach (BluetoothDeviceInfo device in _deviceList.OrderBy(dev => dev.DeviceAddress.ToString()))
                {
                    ListViewItem listViewItem =
                        new ListViewItem(new[] { device.DeviceAddress.ToString(), device.DeviceName })
                        {
                            Tag = device
                        };
                    listViewDevices.Items.Add(listViewItem);
                }
            }
            // select last selected item
            if (_selectedItem == null && listViewDevices.Items.Count > 0)
            {
                _selectedItem = listViewDevices.Items[0];
            }
            if (_selectedItem != null)
            {
                foreach (ListViewItem listViewItem in listViewDevices.Items)
                {
                    if (listViewItem.Tag.GetType() != _selectedItem.Tag.GetType())
                    {
                        continue;
                    }
                    if (string.Compare(listViewItem.SubItems[0].Text, _selectedItem.SubItems[0].Text, StringComparison.Ordinal) == 0)
                    {
                        listViewItem.Selected = true;
                        break;
                    }
                }
            }
            listViewDevices.EndUpdate();
            _ignoreSelection = false;
            UpdateButtonStatus();
        }

        public BluetoothDeviceInfo GetSelectedBtDevice()
        {
            BluetoothDeviceInfo devInfo = null;
            if (listViewDevices.SelectedItems.Count > 0)
            {
                devInfo = listViewDevices.SelectedItems[0].Tag as BluetoothDeviceInfo;
            }
            return devInfo;
        }

        public void UpdateButtonStatus()
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action) UpdateButtonStatus);
                return;
            }
            BluetoothDeviceInfo devInfo = GetSelectedBtDevice();
            buttonSearch.Enabled = !_searching && _cli != null;
            buttonCancel.Enabled = !_searching;
            buttonOk.Enabled = buttonSearch.Enabled && devInfo != null;
        }

        public void UpdateStatusText(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() =>
                {
                    UpdateStatusText(text);
                }));
                return;
            }
            string message = text;
            textBoxStatus.Text = message;
            textBoxStatus.SelectionStart = textBoxStatus.TextLength;
            textBoxStatus.Update();
            textBoxStatus.ScrollToCaret();
        }

        public void PerformSearch()
        {
            if (!buttonSearch.Enabled)
            {
                return;
            }
            if (StartDeviceSearch())
            {
                UpdateButtonStatus();
            }
        }

        private bool IsWinVistaOrHigher()
        {
            OperatingSystem os = Environment.OSVersion;
            return (os.Platform == PlatformID.Win32NT) && (os.Version.Major >= 6);
        }

        private void BluetoothSearch_FormClosed(object sender, FormClosedEventArgs e)
        {
            _bco?.Dispose();
            _cli?.Dispose();
        }

        private void BluetoothSearch_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_searching)
            {
                e.Cancel = true;
            }
        }

        private void listViewDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_ignoreSelection)
            {
                return;
            }
            if (listViewDevices.SelectedItems.Count > 0)
            {
                _selectedItem = listViewDevices.SelectedItems[0];
            }
            UpdateButtonStatus();
        }

        private void buttonSearch_Click(object sender, EventArgs e)
        {
            PerformSearch();
        }

        private void BluetoothSearch_Shown(object sender, EventArgs e)
        {
            UpdateButtonStatus();
            PerformSearch();
        }

        private void listViewDevices_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
        {
            UpdateButtonStatus();
            PerformSearch();
        }

        private void listViewDevices_DoubleClick(object sender, EventArgs e)
        {
            if (listViewDevices.SelectedItems.Count == 1)
            {
                if (buttonOk.Enabled)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            if (_searching)
            {
                DialogResult = DialogResult.None;
            }
        }
    }
}
