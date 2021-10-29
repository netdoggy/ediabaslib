﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PsdzClient.Utility;

namespace PsdzClient.Core
{
	[Serializable]
	public class IStufeXExpression : RuleExpression
	{
		public IStufeXExpression(CompareExpression.ECompareOperator compareOperator, long ilevelid, IStufeXExpression.ILevelyType iLevelType)
		{
			this.iLevelId = ilevelid;
			this.iLevelType = iLevelType;
			this.compareOperator = compareOperator;
		}

		public new static IStufeXExpression Deserialize(Stream ms)
		{
			CompareExpression.ECompareOperator ecompareOperator = (CompareExpression.ECompareOperator)((byte)ms.ReadByte());
			IStufeXExpression.ILevelyType levelyType = (IStufeXExpression.ILevelyType)ms.ReadByte();
			byte[] array = new byte[8];
			ms.Read(array, 0, 8);
			long ilevelid = BitConverter.ToInt64(array, 0);
			return new IStufeXExpression(ecompareOperator, ilevelid, levelyType);
		}

		public override bool Evaluate(Vehicle vec, IFFMDynamicResolver ffmResolver, ValidationRuleInternalResults internalResult)
		{
			if (vec == null)
			{
				return false;
			}
			string ilevelOperand = this.GetILevelOperand(vec);
			string istufeById = ClientContext.Database?.GetIStufeById(this.iLevelId.ToString(CultureInfo.InvariantCulture));
			if (string.IsNullOrEmpty(istufeById))
			{
				return false;
			}
			if (string.IsNullOrEmpty(ilevelOperand) || "0".Equals(ilevelOperand))
			{
				return true;
			}
			string[] ilevelParts = this.GetILevelParts(istufeById);
			if (ilevelParts.Length > 1 && string.Compare(ilevelParts[0], 0, ilevelOperand, 0, ilevelParts[0].Length, StringComparison.OrdinalIgnoreCase) != 0)
			{
				return false;
			}
			bool flag;
			switch (this.compareOperator)
			{
				case CompareExpression.ECompareOperator.EQUAL:
					{
						int? num = FormatConverter.ExtractNumericalILevel(ilevelOperand);
						int? num2 = FormatConverter.ExtractNumericalILevel(istufeById);
						flag = (num.GetValueOrDefault() == num2.GetValueOrDefault() & num != null == (num2 != null));
						break;
					}
				case CompareExpression.ECompareOperator.NOT_EQUAL:
					{
						int? num2 = FormatConverter.ExtractNumericalILevel(ilevelOperand);
						int? num = FormatConverter.ExtractNumericalILevel(istufeById);
						flag = !(num2.GetValueOrDefault() == num.GetValueOrDefault() & num2 != null == (num != null));
						break;
					}
				case CompareExpression.ECompareOperator.GREATER:
					{
						int? num = FormatConverter.ExtractNumericalILevel(ilevelOperand);
						int? num2 = FormatConverter.ExtractNumericalILevel(istufeById);
						flag = (num.GetValueOrDefault() > num2.GetValueOrDefault() & (num != null & num2 != null));
						break;
					}
				case CompareExpression.ECompareOperator.GREATER_EQUAL:
					{
						int? num2 = FormatConverter.ExtractNumericalILevel(ilevelOperand);
						int? num = FormatConverter.ExtractNumericalILevel(istufeById);
						flag = (num2.GetValueOrDefault() >= num.GetValueOrDefault() & (num2 != null & num != null));
						break;
					}
				case CompareExpression.ECompareOperator.LESS:
					{
						int? num = FormatConverter.ExtractNumericalILevel(ilevelOperand);
						int? num2 = FormatConverter.ExtractNumericalILevel(istufeById);
						flag = (num.GetValueOrDefault() < num2.GetValueOrDefault() & (num != null & num2 != null));
						break;
					}
				case CompareExpression.ECompareOperator.LESS_EQUAL:
					{
						int? num2 = FormatConverter.ExtractNumericalILevel(ilevelOperand);
						int? num = FormatConverter.ExtractNumericalILevel(istufeById);
						flag = (num2.GetValueOrDefault() <= num.GetValueOrDefault() & (num2 != null & num != null));
						break;
					}
				default:
					flag = false;
					break;
			}
			return flag;
		}

		public override EEvaluationResult EvaluateVariantRule(ClientDefinition client, CharacteristicSet baseConfiguration, EcuConfiguration ecus)
		{
			return EEvaluationResult.INVALID;
		}

		public override long GetExpressionCount()
		{
			return 1L;
		}

		public override long GetMemorySize()
		{
			return 21L;
		}

		public override void Serialize(MemoryStream ms)
		{
			ms.WriteByte(20);
			ms.WriteByte((byte)this.compareOperator);
			ms.WriteByte((byte)this.iLevelType);
			ms.Write(BitConverter.GetBytes(this.iLevelId), 0, 8);
		}

		public override string ToString()
		{
			string istufeById = ClientContext.Database?.GetIStufeById(this.iLevelId.ToString(CultureInfo.InvariantCulture));
			string ilevelTypeDescription = this.GetILevelTypeDescription();
			return string.Format(CultureInfo.InvariantCulture, "IStufeX: {0}-I-Stufe {1} '{2}' [{3}]", new object[]
			{
				ilevelTypeDescription,
				this.compareOperator,
				istufeById,
				this.iLevelId
			});
		}

		private string GetILevelOperand(Vehicle vehicle)
		{
			switch (this.iLevelType)
			{
				case IStufeXExpression.ILevelyType.HO:
					return vehicle.ILevel;
				case IStufeXExpression.ILevelyType.Factory:
					return vehicle.ILevelWerk;
				case IStufeXExpression.ILevelyType.Ziel:
					return vehicle.TargetILevel;
				default:
					throw new Exception(string.Format("{0} is not a valid I-Level type.", this.iLevelType));
			}
		}

		private string[] GetILevelParts(string iLevel)
		{
			string text = iLevel;
			if (iLevel.Contains("|"))
			{
				text = iLevel.Split(new char[]
				{
					'|'
				})[1];
			}
			return text.Split(new char[]
			{
				'-'
			});
		}

		private string GetILevelTypeDescription()
		{
			switch (this.iLevelType)
			{
				case IStufeXExpression.ILevelyType.HO:
					return "HO";
				case IStufeXExpression.ILevelyType.Factory:
					return "Werk";
				case IStufeXExpression.ILevelyType.Ziel:
					return "Ziel";
				default:
					return "Unknown";
			}
		}

		private readonly CompareExpression.ECompareOperator compareOperator;

		private readonly long iLevelId;

		private readonly IStufeXExpression.ILevelyType iLevelType;

		public enum ILevelyType : byte
		{
			HO,
			Factory,
			Ziel
		}
	}
}
