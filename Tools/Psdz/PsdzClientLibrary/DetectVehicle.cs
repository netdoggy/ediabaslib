﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BmwFileReader;
using EdiabasLib;
using log4net;
using PsdzClient.Core.Container;

namespace PsdzClient
{
    public class DetectVehicle : DetectVehicleBmwBase, IDisposable
    {
        public enum DetectResult
        {
            Ok,
            NoResponse,
            Aborted,
            InvalidDatabase
        }

        private static readonly ILog log = LogManager.GetLogger(typeof(DetectVehicle));

        private static List<JobInfo> ReadVoltageJobsBmwFast = new List<JobInfo>
        {
            new JobInfo("G_MOTOR", "STATUS_LESEN", "ARG;MESSWERTE_IBS2015", "STAT_SPANNUNG_IBS2015_WERT"),
            new JobInfo("G_MOTOR", "STATUS_MESSWERTE_IBS", null, "STAT_U_BATT_WERT"),
        };

        public delegate bool AbortDelegate();
        public delegate void ProgressDelegate(int percent);

        private PsdzDatabase _pdszDatabase;
        private bool _disposed;
        private bool _abortRequest;
        private AbortDelegate _abortFunc;

        public List<PsdzDatabase.EcuInfo> EcuListPsdz { get; private set; }

        public DetectVehicle(PsdzDatabase pdszDatabase, string ecuPath, EdInterfaceEnet.EnetConnection enetConnection = null, bool allowAllocate = true, int addTimeout = 0)
        {
            _pdszDatabase = pdszDatabase;
            EdInterfaceEnet edInterfaceEnet = new EdInterfaceEnet(false);
            _ediabas = new EdiabasNet
            {
                EdInterfaceClass = edInterfaceEnet,
                AbortJobFunc = AbortEdiabasJob
            };
            _ediabas.SetConfigProperty("EcuPath", ecuPath);

            bool icomAllocate = false;
            string hostAddress = "auto:all";
            if (enetConnection != null)
            {
                icomAllocate = allowAllocate && enetConnection.ConnectionType == EdInterfaceEnet.EnetConnection.InterfaceType.Icom;
                hostAddress = enetConnection.ToString();
            }
            edInterfaceEnet.RemoteHost = hostAddress;
            edInterfaceEnet.IcomAllocate = icomAllocate;
            edInterfaceEnet.AddRecTimeoutIcom += addTimeout;

            ResetValues();
        }

        public DetectResult DetectVehicleBmwFast(AbortDelegate abortFunc, ProgressDelegate progressFunc = null, bool detectMotorbikes = false)
        {
            LogInfoFormat("DetectVehicleBmwFast Start");
            ResetValues();
            HashSet<string> invalidSgbdSet = new HashSet<string>();

            try
            {
                List<JobInfo> readVinJobsBmwFast = new List<JobInfo>(ReadVinJobsBmwFast);
                List<JobInfo> readIdentJobsBmwFast = new List<JobInfo>(ReadIdentJobsBmwFast);
                List<JobInfo> readILevelJobsBmwFast = new List<JobInfo>(ReadILevelJobsBmwFast);

                if (!detectMotorbikes)
                {
                    readVinJobsBmwFast.RemoveAll(x => x.Motorbike);
                    readIdentJobsBmwFast.RemoveAll(x => x.Motorbike);
                    readILevelJobsBmwFast.RemoveAll(x => x.Motorbike);
                }

                _abortFunc = abortFunc;
                if (!Connect())
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "DetectVehicleBmwFast Connect failed");
                    return DetectResult.NoResponse;
                }

                int jobCount = readVinJobsBmwFast.Count + readIdentJobsBmwFast.Count + readILevelJobsBmwFast.Count + 1;
                int indexOffset = 0;
                int index = 0;

                List<Dictionary<string, EdiabasNet.ResultData>> resultSets;
                string detectedVin = null;
                JobInfo jobInfoEcuList = null;
                foreach (JobInfo jobInfo in readVinJobsBmwFast)
                {
                    if (_abortRequest)
                    {
                        return DetectResult.Aborted;
                    }

                    try
                    {
                        progressFunc?.Invoke(index * 100 / jobCount);
                        _ediabas.ResolveSgbdFile(jobInfo.SgdbName);

                        _ediabas.ArgString = string.Empty;
                        if (!string.IsNullOrEmpty(jobInfo.JobArgs))
                        {
                            _ediabas.ArgString = jobInfo.JobArgs;
                        }

                        _ediabas.ArgBinaryStd = null;
                        _ediabas.ResultsRequests = string.Empty;
                        _ediabas.ExecuteJob(jobInfo.JobName);

                        invalidSgbdSet.Remove(jobInfo.SgdbName.ToUpperInvariant());
                        if (!string.IsNullOrEmpty(jobInfo.EcuListJob))
                        {
                            jobInfoEcuList = jobInfo;
                        }

                        resultSets = _ediabas.ResultSets;
                        if (resultSets != null && resultSets.Count >= 2)
                        {
                            if (detectedVin == null)
                            {
                                detectedVin = string.Empty;
                            }

                            Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                            if (resultDict.TryGetValue(jobInfo.JobResult, out EdiabasNet.ResultData resultData))
                            {
                                string vin = resultData.OpData as string;
                                // ReSharper disable once AssignNullToNotNullAttribute
                                if (!string.IsNullOrEmpty(vin) && VinRegex.IsMatch(vin))
                                {
                                    detectedVin = vin;
                                    LogInfoFormat("Detected VIN: {0}", detectedVin);
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        invalidSgbdSet.Add(jobInfo.SgdbName.ToUpperInvariant());
                        log.ErrorFormat(CultureInfo.InvariantCulture, "No VIN response");
                        // ignored
                    }

                    index++;
                }

                indexOffset += readVinJobsBmwFast.Count;
                index = indexOffset;

                if (string.IsNullOrEmpty(detectedVin))
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "No VIN detected");
                    return DetectResult.NoResponse;
                }

                Vin = detectedVin;
                PsdzDatabase.VinRanges vinRangesByVin = _pdszDatabase.GetVinRangesByVin17(Vin, GetVin7(Vin), false);
                if (vinRangesByVin != null)
                {
                    if (!string.IsNullOrEmpty(vinRangesByVin.GearboxType))
                    {
                        switch (vinRangesByVin.GearboxType.ToUpperInvariant())
                        {
                            case "A":
                                TransmissonType = "AUT";
                                break;

                            case "M":
                                TransmissonType = "MECH";
                                break;
                        }
                    }
                }

                foreach (JobInfo jobInfo in readIdentJobsBmwFast)
                {
                    if (_abortRequest)
                    {
                        return DetectResult.Aborted;
                    }

                    LogInfoFormat("Read BR job: {0} {1} {2}", jobInfo.SgdbName, jobInfo.JobName, jobInfo.JobArgs ?? string.Empty);
                    if (invalidSgbdSet.Contains(jobInfo.SgdbName.ToUpperInvariant()))
                    {
                        LogInfoFormat("Job ignored: {0}", jobInfo.SgdbName);
                        index++;
                        continue;
                    }

                    try
                    {
                        bool statVcm = string.Compare(jobInfo.JobName, "STATUS_VCM_GET_FA", StringComparison.OrdinalIgnoreCase) == 0;

                        progressFunc?.Invoke(index * 100 / jobCount);
                        _ediabas.ResolveSgbdFile(jobInfo.SgdbName);

                        _ediabas.ArgString = string.Empty;
                        if (!string.IsNullOrEmpty(jobInfo.JobArgs))
                        {
                            _ediabas.ArgString = jobInfo.JobArgs;
                        }

                        _ediabas.ArgBinaryStd = null;
                        _ediabas.ResultsRequests = string.Empty;
                        _ediabas.ExecuteJob(jobInfo.JobName);

                        resultSets = _ediabas.ResultSets;
                        if (resultSets != null && resultSets.Count >= 2)
                        {
                            Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                            if (resultDict.TryGetValue(jobInfo.JobResult, out EdiabasNet.ResultData resultData))
                            {
                                if (!statVcm)
                                {
                                    string fa = resultData.OpData as string;
                                    if (!string.IsNullOrEmpty(fa))
                                    {
                                        _ediabas.ResolveSgbdFile("FA");

                                        _ediabas.ArgString = "1;" + fa;
                                        _ediabas.ArgBinaryStd = null;
                                        _ediabas.ResultsRequests = string.Empty;
                                        _ediabas.ExecuteJob("FA_STREAM2STRUCT");

                                        List<Dictionary<string, EdiabasNet.ResultData>> resultSetsFa = _ediabas.ResultSets;
                                        if (resultSetsFa != null && resultSetsFa.Count >= 2)
                                        {
                                            Dictionary<string, EdiabasNet.ResultData> resultDictFa = resultSetsFa[1];
                                            if (resultDictFa.TryGetValue("STANDARD_FA", out EdiabasNet.ResultData resultStdFa))
                                            {
                                                string stdFaStr = resultStdFa.OpData as string;
                                                if (!string.IsNullOrEmpty(stdFaStr))
                                                {
                                                    StandardFa = stdFaStr;
                                                    SetInfoFromStdFa(stdFaStr);
                                                }
                                            }

                                            if (resultDictFa.TryGetValue("BR", out EdiabasNet.ResultData resultDataBa))
                                            {
                                                string br = resultDataBa.OpData as string;
                                                if (!string.IsNullOrEmpty(br))
                                                {
                                                    LogInfoFormat("Detected BR: {0}", br);
                                                    string vSeries = VehicleInfoBmw.GetVehicleSeriesFromBrName(br, _ediabas);
                                                    if (!string.IsNullOrEmpty(vSeries))
                                                    {
                                                        LogInfoFormat("Detected vehicle series: {0}", vSeries);
                                                        ModelSeries = br;
                                                        Series = vSeries;
                                                    }
                                                }

                                                if (resultDictFa.TryGetValue("C_DATE", out EdiabasNet.ResultData resultDataCDate))
                                                {
                                                    string cDateStr = resultDataCDate.OpData as string;
                                                    DateTime? dateTime = VehicleInfoBmw.ConvertConstructionDate(cDateStr);
                                                    if (dateTime != null)
                                                    {
                                                        LogInfoFormat("Detected construction date: {0}",
                                                            dateTime.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                                                        SetConstructDate(dateTime);
                                                    }
                                                }

                                                if (resultDictFa.TryGetValue("LACK", out EdiabasNet.ResultData resultPaint))
                                                {
                                                    string paintStr = resultPaint.OpData as string;
                                                    if (!string.IsNullOrEmpty(paintStr))
                                                    {
                                                        Paint = paintStr;
                                                    }
                                                }

                                                if (resultDictFa.TryGetValue("POLSTER", out EdiabasNet.ResultData resultUpholstery))
                                                {
                                                    string upholsteryStr = resultUpholstery.OpData as string;
                                                    if (!string.IsNullOrEmpty(upholsteryStr))
                                                    {
                                                        Upholstery = upholsteryStr;
                                                    }
                                                }
                                            }

                                            if (Series != null)
                                            {
                                                SetFaSalpaInfo(resultDictFa);
                                                break;
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    string br = resultData.OpData as string;
                                    if (!string.IsNullOrEmpty(br))
                                    {
                                        LogInfoFormat("Detected BR: {0}", br);
                                        string vSeries = VehicleInfoBmw.GetVehicleSeriesFromBrName(br, _ediabas);
                                        if (!string.IsNullOrEmpty(vSeries))
                                        {
                                            LogInfoFormat("Detected vehicle series: {0}", vSeries);
                                            ModelSeries = br;
                                            Series = vSeries;
                                        }

                                        if (resultDict.TryGetValue("STAT_ZEIT_KRITERIUM", out EdiabasNet.ResultData resultDataCDate))
                                        {
                                            string cDateStr = resultDataCDate.OpData as string;
                                            DateTime? dateTime = VehicleInfoBmw.ConvertConstructionDate(cDateStr);
                                            if (dateTime != null)
                                            {
                                                LogInfoFormat("Detected construction date: {0}",
                                                    dateTime.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                                                SetConstructDate(dateTime);
                                            }
                                        }

                                        if (resultDict.TryGetValue("STAT_LACKCODE", out EdiabasNet.ResultData resultPaint))
                                        {
                                            string paintStr = resultPaint.OpData as string;
                                            if (!string.IsNullOrEmpty(paintStr))
                                            {
                                                Paint = paintStr;
                                            }
                                        }

                                        if (resultDict.TryGetValue("STAT_POLSTERCODE", out EdiabasNet.ResultData resultUpholstery))
                                        {
                                            string upholsteryStr = resultUpholstery.OpData as string;
                                            if (!string.IsNullOrEmpty(upholsteryStr))
                                            {
                                                Upholstery = upholsteryStr;
                                            }
                                        }

                                        if (resultDict.TryGetValue("STAT_TYP_SCHLUESSEL", out EdiabasNet.ResultData resultType))
                                        {
                                            string typeStr = resultType.OpData as string;
                                            if (!string.IsNullOrEmpty(typeStr))
                                            {
                                                TypeKey = typeStr;
                                            }
                                        }
                                    }

                                    if (Series != null)
                                    {
                                        SetStatVcmSalpaInfo(resultSets);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        log.ErrorFormat(CultureInfo.InvariantCulture, "No BR response");
                        // ignored
                    }

                    index++;
                }

                indexOffset += readIdentJobsBmwFast.Count;
                index = indexOffset;

                VehicleStructsBmw.VersionInfo versionInfo = VehicleInfoBmw.GetVehicleSeriesInfoVersion();
                if (versionInfo == null)
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "Vehicle series no version info");
                    return DetectResult.InvalidDatabase;
                }

                PsdzDatabase.DbInfo dbInfo = _pdszDatabase.GetDbInfo();
                if (dbInfo == null)
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "DetectVehicleBmwFast no DbInfo");
                    return DetectResult.InvalidDatabase;
                }

                if (!versionInfo.IsMinVersion(dbInfo.Version, dbInfo.DateTime))
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "DetectVehicleBmwFast Vehicles series too old");
                    return DetectResult.InvalidDatabase;
                }

                VehicleStructsBmw.VehicleSeriesInfo vehicleSeriesInfo = VehicleInfoBmw.GetVehicleSeriesInfo(Series, ConstructYear, ConstructMonth, _ediabas);
                if (vehicleSeriesInfo == null)
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "Vehicle series info not found");
                    return DetectResult.InvalidDatabase;
                }

                LogInfoFormat("Group SGBD: {0}", vehicleSeriesInfo.BrSgbd);
                GroupSgdb = vehicleSeriesInfo.BrSgbd;
                SgdbAddList = vehicleSeriesInfo.SgdbAdd;
                BnType = vehicleSeriesInfo.BnType;
                BrandList = vehicleSeriesInfo.BrandList;

                if (_abortRequest)
                {
                    return DetectResult.Aborted;
                }

                EcuList.Clear();

                try
                {
                    _ediabas.ResolveSgbdFile(GroupSgdb);

                    for (int identRetry = 0; identRetry < 10; identRetry++)
                    {
                        int lastEcuListSize = EcuList.Count;

                        LogInfoFormat("Ecu ident retry: {0}", identRetry + 1);

                        progressFunc?.Invoke(index * 100 / jobCount);
                        _ediabas.ArgString = string.Empty;
                        _ediabas.ArgBinaryStd = null;
                        _ediabas.ResultsRequests = string.Empty;
                        _ediabas.ExecuteJob("IDENT_FUNKTIONAL");

                        resultSets = _ediabas.ResultSets;
                        if (resultSets != null && resultSets.Count >= 2)
                        {
                            int dictIndex = 0;
                            foreach (Dictionary<string, EdiabasNet.ResultData> resultDict in resultSets)
                            {
                                if (dictIndex == 0)
                                {
                                    dictIndex++;
                                    continue;
                                }

                                string ecuName = string.Empty;
                                Int64 ecuAdr = -1;
                                string ecuDesc = string.Empty;
                                string ecuSgbd = string.Empty;
                                string ecuGroup = string.Empty;
                                // ReSharper disable once InlineOutVariableDeclaration
                                EdiabasNet.ResultData resultData;
                                if (resultDict.TryGetValue("ECU_GROBNAME", out resultData))
                                {
                                    if (resultData.OpData is string)
                                    {
                                        ecuName = (string)resultData.OpData;
                                    }
                                }

                                if (resultDict.TryGetValue("ID_SG_ADR", out resultData))
                                {
                                    if (resultData.OpData is Int64)
                                    {
                                        ecuAdr = (Int64)resultData.OpData;
                                    }
                                }

                                if (resultDict.TryGetValue("ECU_NAME", out resultData))
                                {
                                    if (resultData.OpData is string)
                                    {
                                        ecuDesc = (string)resultData.OpData;
                                    }
                                }

                                if (resultDict.TryGetValue("ECU_SGBD", out resultData))
                                {
                                    if (resultData.OpData is string)
                                    {
                                        ecuSgbd = (string)resultData.OpData;
                                    }
                                }

                                if (resultDict.TryGetValue("ECU_GRUPPE", out resultData))
                                {
                                    if (resultData.OpData is string)
                                    {
                                        ecuGroup = (string)resultData.OpData;
                                    }
                                }

                                if (!string.IsNullOrEmpty(ecuName) && ecuAdr >= 0 && ecuAdr <= VehicleStructsBmw.MaxEcuAddr && !string.IsNullOrEmpty(ecuSgbd))
                                {
                                    if (EcuList.All(ecuInfo => ecuInfo.Address != ecuAdr))
                                    {
                                        EcuInfo ecuInfo = new EcuInfo(ecuName, ecuAdr, ecuGroup);
                                        EcuList.Add(ecuInfo);
                                    }
                                }

                                dictIndex++;
                            }
                        }

                        LogInfoFormat("Detect EcuListSize={0}, EcuListSizeOld={1}", EcuList.Count, lastEcuListSize);
                        if (EcuList.Count == lastEcuListSize)
                        {
                            break;
                        }

                        indexOffset++;
                        jobCount++;
                        index++;
                    }
                }
                catch (Exception)
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "No ident response");
                    return DetectResult.NoResponse;
                }

                if (jobInfoEcuList != null)
                {
                    //EcuList.Clear();
                    if (_abortRequest)
                    {
                        return DetectResult.Aborted;
                    }

                    List<EcuInfo> ecuInfoAddList = new List<EcuInfo>();
                    try
                    {
                        progressFunc?.Invoke(index * 100 / jobCount);
                        _ediabas.ResolveSgbdFile(jobInfoEcuList.SgdbName);

                        _ediabas.ArgString = string.Empty;
                        _ediabas.ArgBinaryStd = null;
                        _ediabas.ResultsRequests = string.Empty;
                        _ediabas.ExecuteJob(jobInfoEcuList.EcuListJob);

                        resultSets = _ediabas.ResultSets;
                        if (resultSets != null && resultSets.Count >= 2)
                        {
                            int dictIndex = 0;
                            foreach (Dictionary<string, EdiabasNet.ResultData> resultDict in resultSets)
                            {
                                if (dictIndex == 0)
                                {
                                    dictIndex++;
                                    continue;
                                }

                                string ecuName = string.Empty;
                                Int64 ecuAdr = -1;
                                // ReSharper disable once InlineOutVariableDeclaration
                                EdiabasNet.ResultData resultData;
                                if (resultDict.TryGetValue("STAT_SG_NAME_TEXT", out resultData))
                                {
                                    if (resultData.OpData is string)
                                    {
                                        ecuName = (string)resultData.OpData;
                                    }
                                }

                                if (resultDict.TryGetValue("STAT_SG_DIAG_ADRESSE", out resultData))
                                {
                                    if (resultData.OpData is string)
                                    {
                                        string ecuAdrStr = (string)resultData.OpData;
                                        if (!string.IsNullOrEmpty(ecuAdrStr) && ecuAdrStr.Length > 1)
                                        {
                                            string hexString = ecuAdrStr.Trim().Substring(2);
                                            if (Int32.TryParse(hexString, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out Int32 addrValue))
                                            {
                                                ecuAdr = addrValue;
                                            }
                                        }
                                    }
                                }

                                if (!string.IsNullOrEmpty(ecuName) && ecuAdr >= 0 && ecuAdr <= VehicleStructsBmw.MaxEcuAddr)
                                {
                                    if (EcuList.All(ecuInfo => ecuInfo.Address != ecuAdr) &&
                                        ecuInfoAddList.All(ecuInfo => ecuInfo.Address != ecuAdr))
                                    {
                                        string groupSgbd = null;
                                        if (vehicleSeriesInfo.EcuList != null)
                                        {
                                            foreach (VehicleStructsBmw.VehicleEcuInfo vehicleEcuInfo in vehicleSeriesInfo.EcuList)
                                            {
                                                if (vehicleEcuInfo.DiagAddr == ecuAdr)
                                                {
                                                    groupSgbd = vehicleEcuInfo.GroupSgbd;
                                                    break;
                                                }
                                            }
                                        }

                                        if (!string.IsNullOrEmpty(groupSgbd))
                                        {
                                            EcuInfo ecuInfo = new EcuInfo(ecuName, ecuAdr, groupSgbd);
                                            ecuInfoAddList.Add(ecuInfo);
                                        }
                                    }
                                }

                                dictIndex++;
                            }
                        }

                        foreach (EcuInfo ecuInfoAdd in ecuInfoAddList)
                        {
                            string groupSgbd = ecuInfoAdd.Grp;
                            try
                            {
                                _ediabas.ResolveSgbdFile(groupSgbd);

                                _ediabas.ArgString = string.Empty;
                                _ediabas.ArgBinaryStd = null;
                                _ediabas.ResultsRequests = string.Empty;
                                _ediabas.ExecuteJob("_VERSIONINFO");

                                string ecuDesc = GetEcuName(_ediabas.ResultSets);
                                string ecuSgbd = Path.GetFileNameWithoutExtension(_ediabas.SgbdFileName);
                                ecuInfoAdd.Sgbd = ecuSgbd;
                                ecuInfoAdd.Description = ecuDesc;

                                EcuList.Add(ecuInfoAdd);
                            }
                            catch (Exception)
                            {
                                _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Failed to resolve Group {0}", groupSgbd);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        log.ErrorFormat(CultureInfo.InvariantCulture, "No ecu list response");
                        // ignored
                    }

                    indexOffset++;
                    jobCount++;
                    index++;
                }

                string iLevelShip = null;
                string iLevelCurrent = null;
                string iLevelBackup = null;
                foreach (JobInfo jobInfo in readILevelJobsBmwFast)
                {
                    if (_abortRequest)
                    {
                        return DetectResult.Aborted;
                    }

                    LogInfoFormat("Read ILevel job: {0},{1}", jobInfo.SgdbName, jobInfo.JobName);
                    if (invalidSgbdSet.Contains(jobInfo.SgdbName.ToUpperInvariant()))
                    {
                        LogInfoFormat("Job ignored: {0}", jobInfo.SgdbName);
                        continue;
                    }

                    try
                    {
                        progressFunc?.Invoke(index * 100 / jobCount);
                        _ediabas.ResolveSgbdFile(jobInfo.SgdbName);

                        _ediabas.ArgString = string.Empty;
                        _ediabas.ArgBinaryStd = null;
                        _ediabas.ResultsRequests = string.Empty;
                        _ediabas.ExecuteJob(jobInfo.JobName);

                        resultSets = _ediabas.ResultSets;
                        if (resultSets != null && resultSets.Count >= 2)
                        {
                            Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                            if (resultDict.TryGetValue("STAT_I_STUFE_WERK", out EdiabasNet.ResultData resultData))
                            {
                                string iLevel = resultData.OpData as string;
                                if (!string.IsNullOrEmpty(iLevel) && iLevel.Length >= 4 &&
                                    string.Compare(iLevel, VehicleInfoBmw.ResultUnknown, StringComparison.OrdinalIgnoreCase) != 0)
                                {
                                    iLevelShip = iLevel;
                                    LogInfoFormat("Detected ILevel ship: {0}",
                                        iLevelShip);
                                }
                            }

                            if (!string.IsNullOrEmpty(iLevelShip))
                            {
                                if (resultDict.TryGetValue("STAT_I_STUFE_HO", out resultData))
                                {
                                    string iLevel = resultData.OpData as string;
                                    if (!string.IsNullOrEmpty(iLevel) && iLevel.Length >= 4 &&
                                        string.Compare(iLevel, VehicleInfoBmw.ResultUnknown, StringComparison.OrdinalIgnoreCase) != 0)
                                    {
                                        iLevelCurrent = iLevel;
                                        LogInfoFormat("Detected ILevel current: {0}",
                                            iLevelCurrent);
                                    }
                                }

                                if (string.IsNullOrEmpty(iLevelCurrent))
                                {
                                    iLevelCurrent = iLevelShip;
                                }

                                if (resultDict.TryGetValue("STAT_I_STUFE_HO_BACKUP", out resultData))
                                {
                                    string iLevel = resultData.OpData as string;
                                    if (!string.IsNullOrEmpty(iLevel) && iLevel.Length >= 4 &&
                                        string.Compare(iLevel, VehicleInfoBmw.ResultUnknown, StringComparison.OrdinalIgnoreCase) != 0)
                                    {
                                        iLevelBackup = iLevel;
                                        LogInfoFormat("Detected ILevel backup: {0}",
                                            iLevelBackup);
                                    }
                                }

                                break;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        log.ErrorFormat(CultureInfo.InvariantCulture, "No ILevel response");
                        // ignored
                    }
                }

                indexOffset += readILevelJobsBmwFast.Count;
                progressFunc?.Invoke(100);

                if (string.IsNullOrEmpty(iLevelShip))
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "ILevel not found");
                    return DetectResult.NoResponse;
                }

                LogInfoFormat("ILevel: Ship={0}, Current={1}, Backup={2}", iLevelShip,
                    iLevelCurrent, iLevelBackup);

                ILevelShip = iLevelShip;
                ILevelCurrent = iLevelCurrent;
                ILevelBackup = iLevelBackup;

                HandleSpecialEcus();
                ConvertEcuList();

                if (_abortRequest)
                {
                    return DetectResult.Aborted;
                }

                LogInfoFormat("DetectVehicleBmwFast Finish");
                return DetectResult.Ok;
            }
            catch (Exception ex)
            {
                log.ErrorFormat(CultureInfo.InvariantCulture, "DetectVehicleBmwFast Exception: {0}", ex.Message);
                return DetectResult.NoResponse;
            }
            finally
            {
                _abortFunc = null;
            }
        }

        public double ReadBatteryVoltage(AbortDelegate abortFunc)
        {
            double voltage = -1;

            try
            {
                _abortFunc = abortFunc;
                if (!Connect())
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "ReadBatteryVoltage Connect failed");
                    return -1;
                }

                foreach (JobInfo jobInfo in ReadVoltageJobsBmwFast)
                {
                    if (_abortRequest)
                    {
                        return -1;
                    }

                    LogInfoFormat("Read voltage job: {0}, {1}, {2}",
                        jobInfo.SgdbName, jobInfo.JobName, jobInfo.JobArgs ?? string.Empty);

                    try
                    {
                        _ediabas.ResolveSgbdFile(jobInfo.SgdbName);

                        _ediabas.ArgString = string.Empty;
                        if (!string.IsNullOrEmpty(jobInfo.JobArgs))
                        {
                            _ediabas.ArgString = jobInfo.JobArgs;
                        }

                        _ediabas.ArgBinaryStd = null;
                        _ediabas.ResultsRequests = string.Empty;
                        _ediabas.ExecuteJob(jobInfo.JobName);

                        List<Dictionary<string, EdiabasNet.ResultData>> resultSets = _ediabas.ResultSets;
                        if (resultSets != null && resultSets.Count >= 2)
                        {
                            Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                            if (resultDict.TryGetValue(jobInfo.JobResult, out EdiabasNet.ResultData resultData))
                            {
                                if (resultData.OpData is Double)
                                {
                                    voltage = (double)resultData.OpData;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        log.ErrorFormat(CultureInfo.InvariantCulture, "No voltage response");
                        // ignored
                    }
                }
            }
            catch (Exception ex)
            {
                log.ErrorFormat(CultureInfo.InvariantCulture, "ReadBatteryVoltage Exception: {0}", ex.Message);
                return -1;
            }
            finally
            {
                _abortFunc = null;
            }

            return voltage;
        }

        public string ExecuteContainerXml(AbortDelegate abortFunc, string configurationContainerXml, Dictionary<string,string> runOverrideDict = null)
        {
            string result = string.Empty;

            try
            {
                _abortFunc = abortFunc;
                if (!Connect())
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "ExecuteContainerXml Connect failed");
                    return null;
                }

                ConfigurationContainer configurationContainer = ConfigurationContainer.Deserialize(configurationContainerXml);
                if (configurationContainer == null)
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "ExecuteContainerXml Deserialize failed");
                    return null;
                }

                if (runOverrideDict != null)
                {
                    foreach (KeyValuePair<string, string> runOverride in runOverrideDict)
                    {
                        configurationContainer.AddRunOverride(runOverride.Key, runOverride.Value);
                    }
                }

                EDIABASAdapter ediabasAdapter = new EDIABASAdapter(true, new ECUKom("DetectVehicle", _ediabas), configurationContainer);
                ediabasAdapter.DoParameterization();
                IDiagnosticDeviceResult diagnosticDeviceResult = ediabasAdapter.Execute(new ParameterContainer());
                if (diagnosticDeviceResult == null)
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "ExecuteContainerXml Execute failed");
                    return null;
                }

                if (diagnosticDeviceResult.Error != null && diagnosticDeviceResult.ECUJob != null && diagnosticDeviceResult.ECUJob.JobErrorCode != 0)
                {
                    string jobErrorText = diagnosticDeviceResult.ECUJob.JobErrorText;
                    log.ErrorFormat(CultureInfo.InvariantCulture, "ExecuteContainerXml Job error: {0}", jobErrorText);
                    return jobErrorText;
                }

                bool jobOk = false;
                if (diagnosticDeviceResult.ECUJob != null && diagnosticDeviceResult.ECUJob.JobResultSets > 0)
                {
                    jobOk = diagnosticDeviceResult.ECUJob.IsOkay((ushort)diagnosticDeviceResult.ECUJob.JobResultSets);
                }

                if (!jobOk && diagnosticDeviceResult.ECUJob != null)
                {
                    string stringResult = diagnosticDeviceResult.ECUJob.getStringResult((ushort)diagnosticDeviceResult.ECUJob.JobResultSets, "JOB_STATUS");
                    log.ErrorFormat(CultureInfo.InvariantCulture, "ExecuteContainerXml Job status: {0}", stringResult);
                    return stringResult;
                }

                LogInfoFormat("ExecuteContainerXml Job OK: {0}", jobOk);
                string jobStatus = diagnosticDeviceResult.getISTAResultAsType("/Result/Status/JOB_STATUS", typeof(string)) as string;
                if (jobStatus != null)
                {
                    result = jobStatus;
                    if (jobStatus != "OKAY")
                    {
                        log.ErrorFormat(CultureInfo.InvariantCulture, "ExecuteContainerXml Job status: {0}", jobStatus);
                        return jobStatus;
                    }
                }
            }
            catch (Exception ex)
            {
                log.ErrorFormat(CultureInfo.InvariantCulture, "ExecuteContainerXml Exception: {0}", ex.Message);
                return null;
            }
            finally
            {
                _abortFunc = null;
            }

            return result;
        }

        public static string ConvertContainerXml(string configurationContainerXml, Dictionary<string, string> runOverrideDict = null)
        {
            try
            {
                ConfigurationContainer configurationContainer = ConfigurationContainer.Deserialize(configurationContainerXml);
                if (configurationContainer == null)
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "ConvertContainerXml Deserialize failed");
                    return null;
                }

                if (runOverrideDict != null)
                {
                    foreach (KeyValuePair<string, string> runOverride in runOverrideDict)
                    {
                        configurationContainer.AddRunOverride(runOverride.Key, runOverride.Value);
                    }
                }

                EDIABASAdapter ediabasAdapter = new EDIABASAdapter(true, null, configurationContainer);
                ediabasAdapter.DoParameterization();
                if (string.IsNullOrEmpty(ediabasAdapter.EcuGroup))
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "ConvertContainerXml Empty EcuGroup");
                    return null;
                }

                if (string.IsNullOrEmpty(ediabasAdapter.EcuJob))
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "ConvertContainerXml Empty EcuJob");
                    return null;
                }

                bool binMode = ediabasAdapter.IsBinModeRequired;
                if (binMode && ediabasAdapter.EcuData == null)
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "ConvertContainerXml Binary data missing");
                    return null;
                }

                StringBuilder sb = new StringBuilder();
                sb.Append(ediabasAdapter.EcuGroup);
                sb.Append("#");
                sb.Append(ediabasAdapter.EcuJob);

                string paramString;
                if (binMode)
                {
                    paramString = "|" + BitConverter.ToString(ediabasAdapter.EcuData).Replace("-", "");
                }
                else
                {
                    paramString = ediabasAdapter.EcuParam;
                }

                string resultFilterString = ediabasAdapter.EcuResultFilter;
                if (!string.IsNullOrEmpty(paramString) || !string.IsNullOrEmpty(resultFilterString))
                {
                    sb.Append("#");
                    if (!string.IsNullOrEmpty(paramString))
                    {
                        sb.Append(paramString);
                    }

                    if (!string.IsNullOrEmpty(resultFilterString))
                    {
                        sb.Append("#");
                        sb.Append(resultFilterString);
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                log.ErrorFormat(CultureInfo.InvariantCulture, "ConvertContainerXml Exception: {0}", ex.Message);
                return null;
            }
        }

        public bool Connect()
        {
            try
            {
                return _ediabas.EdInterfaceClass.InterfaceConnect();
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool Disconnect()
        {
            try
            {
                return _ediabas.EdInterfaceClass.InterfaceDisconnect();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool AbortEdiabasJob()
        {
            if (_abortFunc != null)
            {
                if (_abortFunc.Invoke())
                {
                    _abortRequest = true;
                }
            }

            return _abortRequest;
        }

        private void ConvertEcuList()
        {
            EcuListPsdz.Clear();
            foreach (EcuInfo ecuInfo in EcuList)
            {
                EcuListPsdz.Add(new PsdzDatabase.EcuInfo(ecuInfo.Name, ecuInfo.Address, ecuInfo.Description, ecuInfo.Sgbd, ecuInfo.Grp));
            }
        }

        protected override void ResetValues()
        {
            base.ResetValues();
            _abortRequest = false;
            _abortFunc = null;
            EcuListPsdz = new List<PsdzDatabase.EcuInfo>();
        }

        protected override string GetEcuNameByIdent(string sgbd)
        {
            try
            {
                _ediabas.ResolveSgbdFile(sgbd);

                _ediabas.ArgString = string.Empty;
                _ediabas.ArgBinaryStd = null;
                _ediabas.ResultsRequests = string.Empty;
                _ediabas.ExecuteJob("IDENT");

                string ecuName = Path.GetFileNameWithoutExtension(_ediabas.SgbdFileName);
                return ecuName.ToUpperInvariant();
            }
            catch (Exception)
            {
                // ignored
            }

            return null;
        }

        protected override void LogInfoFormat(string format, params object[] args)
        {
            log.InfoFormat(CultureInfo.InvariantCulture, format, args);
        }

        protected override void LogErrorFormat(string format, params object[] args)
        {
            log.ErrorFormat(CultureInfo.InvariantCulture, format, args);
        }

        public void Dispose()
        {
            Dispose(true);
            // This object will be cleaned up by the Dispose method.
            // Therefore, you should call GC.SupressFinalize to
            // take this object off the finalization queue
            // and prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (!_disposed)
            {
                if (_ediabas != null)
                {
                    _ediabas.Dispose();
                    _ediabas = null;
                }

                // If disposing equals true, dispose all managed
                // and unmanaged resources.
                if (disposing)
                {
                }

                // Note disposing has been done.
                _disposed = true;
            }
        }
    }
}
