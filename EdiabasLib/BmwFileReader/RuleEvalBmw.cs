﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Android.Util;
using BmwDeepObd;
using EdiabasLib;

namespace BmwFileReader
{
    public class RuleEvalBmw
    {
        public enum RuleType
        {
            Fault,
            EcuFunc,
            DiagObj
        }

#if DEBUG
        private static readonly string Tag = typeof(RuleEvalBmw).FullName;
#endif
        private RulesInfo _rulesInfo { get; }
        private readonly Dictionary<string, List<string>> _propertiesDict = new Dictionary<string, List<string>>();
        private readonly HashSet<string> _unknownNamesHash = new HashSet<string>();
        private ulong? _unknownId;
        private readonly object _lockObject = new object();

        public RuleEvalBmw()
        {
            _rulesInfo = new RulesInfo(this);
        }

        public bool EvaluateRule(string id, RuleType ruleType)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            if (!ulong.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong idValue))
            {
#if DEBUG
                Log.Info(Tag, string.Format("EvaluateRule Convert id failed: '{0}'", id));
#endif
                return true;
            }

            lock (_lockObject)
            {
                if (_rulesInfo == null)
                {
                    return false;
                }

                try
                {
                    _unknownNamesHash.Clear();
                    _unknownId = null;

                    bool valid = false;
                    switch (ruleType)
                    {
                        case RuleType.Fault:
                            valid = _rulesInfo.IsFaultRuleValid(idValue);
                            break;

                        case RuleType.EcuFunc:
                            valid = _rulesInfo.IsEcuFuncRuleValid(idValue);
                            break;

                        case RuleType.DiagObj:
                            valid = _rulesInfo.IsDiagObjectRuleValid(idValue);
                            break;
                    }

                    if (_unknownId != null)
                    {
                        return true;
                    }

                    if (_unknownNamesHash.Count > 0)
                    {
#if DEBUG
                        StringBuilder sbDebug = new StringBuilder();
                        foreach (string unknownHash in _unknownNamesHash)
                        {
                            if (sbDebug.Length > 0)
                            {
                                sbDebug.Append(", ");
                            }
                            sbDebug.Append(unknownHash);
                        }

                        Log.Info(Tag, string.Format("EvaluateRule Unknown rules: '{0}'", sbDebug));
#endif
                        return true;
                    }

                    return valid;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public void ClearEvalProperties()
        {
            lock (_lockObject)
            {
                _unknownNamesHash.Clear();
                _propertiesDict.Clear();
            }
        }

        public bool SetEvalProperties(DetectVehicleBmw detectVehicleBmw, EcuFunctionStructs.EcuVariant ecuVariant)
        {
            lock (_lockObject)
            {
                try
                {
                    _propertiesDict.Clear();

                    if (detectVehicleBmw == null || !detectVehicleBmw.Valid)
                    {
                        return false;
                    }

                    if (detectVehicleBmw.TypeKeyProperties != null)
                    {
                        foreach (KeyValuePair<string,string> propertyPair in detectVehicleBmw.TypeKeyProperties)
                        {
                            string value = propertyPair.Value ?? string.Empty;
                            _propertiesDict.TryAdd(propertyPair.Key.ToUpperInvariant(), new List<string> { value.Trim() });
                        }
                    }

                    // this should be the dealer languge, replace it with current language
                    string language = ActivityCommon.GetCurrentLanguageStatic();
                    if (language.Length > 2)
                    {
                        language = language.Substring(0, 2);
                    }
                    _propertiesDict.TryAdd("Country".ToUpperInvariant(), new List<string> { language.ToUpperInvariant() });

                    if (!string.IsNullOrEmpty(detectVehicleBmw.Brand))
                    {
                        _propertiesDict.TryAdd("Marke".ToUpperInvariant(), new List<string> { detectVehicleBmw.Brand.Trim() });
                    }

                    if (!string.IsNullOrWhiteSpace(detectVehicleBmw.TypeKey))
                    {
                        _propertiesDict.TryAdd("Typschl?ssel", new List<string> { detectVehicleBmw.TypeKey.Trim() });
                    }

                    if (!string.IsNullOrWhiteSpace(detectVehicleBmw.Series))
                    {
                        _propertiesDict.TryAdd("E-Bezeichnung".ToUpperInvariant(), new List<string> { detectVehicleBmw.Series.Trim() });
                    }

                    List<string> salapa = new List<string>();
                    if (detectVehicleBmw.Salapa != null)
                    {
                        salapa.AddRange(detectVehicleBmw.Salapa);
                    }

                    if (detectVehicleBmw.HoWords != null)
                    {
                        salapa.AddRange(detectVehicleBmw.HoWords);
                    }

                    if (detectVehicleBmw.EWords != null)
                    {
                        salapa.AddRange(detectVehicleBmw.EWords);
                    }

                    _propertiesDict.TryAdd("SALAPA".ToUpperInvariant(), salapa);
                    _propertiesDict.TryAdd("ProtectionVehicleService".ToUpperInvariant(), new List<string> { "1" });

                    if (!string.IsNullOrWhiteSpace(detectVehicleBmw.ConstructYear))
                    {
                        string constructDate = detectVehicleBmw.ConstructYear;
                        if (!string.IsNullOrWhiteSpace(detectVehicleBmw.ConstructMonth))
                        {
                            constructDate += detectVehicleBmw.ConstructMonth;
                        }
                        else
                        {
                            constructDate += "01";
                        }
                        _propertiesDict.TryAdd("Baustand".ToUpperInvariant(), new List<string> { constructDate });

                        string productionDate = constructDate;
                        if (!string.IsNullOrWhiteSpace(detectVehicleBmw.ILevelShip))
                        {
                            string iLevelTrim = detectVehicleBmw.ILevelShip.Trim();
                            string[] levelParts = iLevelTrim.Split("-");
                            if (levelParts.Length == 4)
                            {
                                if (Int32.TryParse(levelParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iLevelYear) &&
                                    Int32.TryParse(levelParts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iLevelMonth))
                                {
                                    int dateValue = (iLevelYear + 2000) * 100 + iLevelMonth;
                                    productionDate = dateValue.ToString(CultureInfo.InvariantCulture);
                                }
                            }
                        }

                        _propertiesDict.TryAdd("Produktionsdatum".ToUpperInvariant(), new List<string> { productionDate });
                    }

                    string iStufe = string.Empty;
                    string iStufeX = string.Empty;
                    string br = string.Empty;

                    if (!string.IsNullOrWhiteSpace(detectVehicleBmw.ILevelCurrent))
                    {
                        string iLevelTrim = detectVehicleBmw.ILevelCurrent.Trim();
                        string[] levelParts = iLevelTrim.Split("-");
                        iStufe = iLevelTrim;

                        if (levelParts.Length == 4 && iLevelTrim.Length == 14)
                        {
                            string iLevelNum = levelParts[1] + levelParts[2] + levelParts[3];
                            if (Int32.TryParse(iLevelNum, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iLevelValue))
                            {
                                iStufeX = iLevelValue.ToString(CultureInfo.InvariantCulture);
                            }

                            br = levelParts[0];
                        }
                    }

                    _propertiesDict.TryAdd("IStufe".ToUpperInvariant(), new List<string> { iStufe });
                    _propertiesDict.TryAdd("IStufeX".ToUpperInvariant(), new List<string> { iStufeX });
                    _propertiesDict.TryAdd("Baureihenverbund".ToUpperInvariant(), new List<string> { br });

                    return SetEvalEcuProperties(ecuVariant);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public bool UpdateEvalEcuProperties(EcuFunctionStructs.EcuVariant ecuVariant)
        {
            lock (_lockObject)
            {
                return SetEvalEcuProperties(ecuVariant);
            }
        }

        private bool SetEvalEcuProperties(EcuFunctionStructs.EcuVariant ecuVariant)
        {
            try
            {
                string keyEcuRep = "EcuRepresentative".ToUpperInvariant();
                string keyEcuClique = "EcuClique".ToUpperInvariant();
                string keyEcuVariant = "EcuVariant".ToUpperInvariant();

                _propertiesDict.Remove(keyEcuRep);
                _propertiesDict.Remove(keyEcuClique);
                _propertiesDict.Remove(keyEcuVariant);

                if (ecuVariant != null)
                {
                    string repsName = ecuVariant.EcuClique?.EcuRepsName;
                    if (!string.IsNullOrEmpty(repsName))
                    {
#if DEBUG
                        Log.Info(Tag, string.Format("SetEvalEcuProperties '{0}'='{1}'", keyEcuRep, repsName));
#endif
                        _propertiesDict.Add(keyEcuRep, new List<string> { repsName });
                    }

                    string cliqueName = ecuVariant.EcuClique?.CliqueName;
                    if (!string.IsNullOrEmpty(cliqueName))
                    {
#if DEBUG
                        Log.Info(Tag, string.Format("SetEvalEcuProperties '{0}'='{1}'", keyEcuClique, cliqueName));
#endif
                        _propertiesDict.Add(keyEcuClique, new List<string> { cliqueName });
                    }

                    string ecuName = ecuVariant.EcuName;
                    if (!string.IsNullOrEmpty(ecuName))
                    {
#if DEBUG
                        Log.Info(Tag, string.Format("SetEvalEcuProperties '{0}'='{1}'", keyEcuVariant, ecuName));
#endif
                        _propertiesDict.Add(keyEcuVariant, new List<string> { ecuName });
                    }
#if DEBUG
                    List<string> missingRules = GetMissingRules();
                    if (missingRules.Count > 0)
                    {
                        StringBuilder sbMissing = new StringBuilder();
                        foreach (string rule in missingRules)
                        {
                            if (sbMissing.Length > 0)
                            {
                                sbMissing.Append(", ");
                            }

                            sbMissing.Append(rule);
                        }

                        Log.Info(Tag, string.Format("SetEvalEcuProperties Missing rules: {0}", sbMissing));
                    }
                    else
                    {
                        Log.Info(Tag, "SetEvalEcuProperties All rules present");
                    }
#endif
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        public List<string> GetMissingRules()
        {
            List<string> missingRules = new List<string>();
            foreach (string ruleName in RulesInfo.RuleNames)
            {
                if (!_propertiesDict.ContainsKey(ruleName.ToUpperInvariant()))
                {
                    missingRules.Add(ruleName);
                }
            }

            return missingRules;
        }

        public void RuleNotFound(ulong id)
        {
            _unknownId = id;
        }

        public string RuleString(string name)
        {
            string propertyString = GetPropertyString(name);
            if (string.IsNullOrWhiteSpace(propertyString))
            {
                return string.Empty;
            }
            return propertyString;
        }

        public long RuleNum(string name)
        {
            long? propertyValue = GetPropertyValue(name);
            if (!propertyValue.HasValue)
            {
                return -1;
            }

            return propertyValue.Value;
        }

        public bool IsValidRuleString(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

#if DEBUG
            // ReSharper disable once ReplaceWithSingleAssignment.False
            bool logInfo = false;
            if (string.Compare(name, "EcuRepresentative", StringComparison.OrdinalIgnoreCase) == 0)
            {
                logInfo = true;
            }
#if false
            if (string.Compare(name, "EcuClique", StringComparison.OrdinalIgnoreCase) == 0)
            {
                logInfo = true;
            }
#endif
#endif

            List<string> propertyStrings = GetPropertyStrings(name);
            if (propertyStrings == null)
            {
                return false;
            }

            string valueTrim = value.Trim();
            foreach (string propertyString in propertyStrings)
            {
                if (string.Compare(propertyString.Trim(), valueTrim, StringComparison.OrdinalIgnoreCase) == 0)
                {
#if DEBUG
                    if (logInfo)
                    {
                        Log.Info(Tag, string.Format("IsValidRuleString {0}: '{1}'=='{2}'", name, propertyString, valueTrim));
                    }
#endif
                    return true;
                }
#if DEBUG
                if (logInfo)
                {
                    Log.Info(Tag, string.Format("IsValidRuleString {0}: '{1}'!='{2}'", name, propertyString, valueTrim));
                }
#endif
            }

            string valueAsc = VehicleInfoBmw.RemoveNonAsciiChars(valueTrim);
            if (!string.IsNullOrEmpty(valueAsc) && valueAsc != valueTrim)
            {
#if DEBUG
                if (logInfo)
                {
                    Log.Info(Tag, string.Format("IsValidRuleString {0}: '{1}'->'{2}'", name, valueAsc, valueTrim));
                }
#endif
                foreach (string propertyString in propertyStrings)
                {
                    if (string.Compare(propertyString.Trim(), valueAsc, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool IsValidRuleNum(string name, long value)
        {
            List<long> propertyValues = GetPropertyValues(name);
            if (propertyValues == null)
            {
                return false;
            }

            foreach (long propertyValue in propertyValues)
            {
                if (propertyValue == value)
                {
                    return true;
                }
            }

            return false;
        }

        private string GetPropertyString(string name)
        {
            List<string> stringList = GetPropertyStrings(name);
            if (stringList != null && stringList.Count > 0)
            {
                return stringList[0];
            }

            return string.Empty;
        }

        private List<string> GetPropertyStrings(string name)
        {
            if (_propertiesDict == null)
            {
                return null;
            }

            string key = name.Trim().ToUpperInvariant();
            if (_propertiesDict.TryGetValue(key, out List<string> valueList))
            {
                return valueList;
            }

            _unknownNamesHash.Add(key);
            return null;
        }

        private long? GetPropertyValue(string name)
        {
            List<long> propertyValues = GetPropertyValues(name);
            if (propertyValues != null && propertyValues.Count > 0)
            {
                return propertyValues[0];
            }

            return null;
        }

        private List<long> GetPropertyValues(string name)
        {
            List<string> propertyStrings = GetPropertyStrings(name);
            if (propertyStrings == null)
            {
                return null;
            }

            List<long> valueList = new List<long>();
            foreach (string propertyString in propertyStrings)
            {
                if (long.TryParse(propertyString, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
                {
                    if (!valueList.Contains(result))
                    {
                        valueList.Add(result);
                    }
                }
            }

            return valueList;
        }
    }
}
