﻿using System;
using System.Globalization;
using System.Windows.Forms;

using BizHawk.Common;
using BizHawk.Common.NumberExtensions;

namespace BizHawk.Client.EmuHawk
{
	// TODO: add a MaxValue property, nullable int, that will show up in Designer, change events will check that value and fix entries that exceed that value
	public interface INumberBox
	{
		bool Nullable { get; }
		int? ToRawInt();
		void SetFromRawInt(int? rawint);
	}

	public class HexTextBox : TextBox, INumberBox
	{
		private string _addressFormatStr = string.Empty;
		private int? _maxSize;
		private bool _nullable = true;

		public HexTextBox()
		{
			CharacterCasing = CharacterCasing.Upper;
		}

		public bool Nullable { get { return _nullable; } set { _nullable = value; } }

		public void SetHexProperties(int domainSize)
		{
			_maxSize = domainSize - 1;
			MaxLength = _maxSize.Value.NumHexDigits();
			_addressFormatStr = "{0:X" + MaxLength + "}";
			
			ResetText();
		}

		private uint GetMax()
		{
			if (_maxSize.HasValue)
			{
				return (uint)_maxSize.Value;
			}

            return (uint)(((long)1 << (4 * MaxLength)) - 1);
		}

		public override void ResetText()
		{
			Text = _nullable ? string.Empty : string.Format(_addressFormatStr, 0);
		}

		protected override void OnKeyPress(KeyPressEventArgs e)
		{
			if (e.KeyChar == '\b' || e.KeyChar == 22 || e.KeyChar == 1 || e.KeyChar == 3)
			{
				return;
			}
			
			if (!InputValidate.IsHex(e.KeyChar))
			{
				e.Handled = true;
			}
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Up)
			{
				if (InputValidate.IsHex(Text) && !string.IsNullOrEmpty(_addressFormatStr))
				{
					var val = (uint)ToRawInt();

					if (val == GetMax())
					{
						val = 0;
					}
					else
					{
						val++;
					}

					Text = string.Format(_addressFormatStr, val);
				}
			}
			else if (e.KeyCode == Keys.Down)
			{
				if (InputValidate.IsHex(Text) && !string.IsNullOrEmpty(_addressFormatStr))
				{
					var val = (uint)ToRawInt();
					if (val == 0)
					{
						val = GetMax();
					}
					else
					{
						val--;
					}

					Text = string.Format(_addressFormatStr, val);
				}
			}
			else
			{
				base.OnKeyDown(e);
			}
		}

		protected override void OnTextChanged(EventArgs e)
		{
			if (string.IsNullOrWhiteSpace(Text))
			{
				ResetText();
			}

			base.OnTextChanged(e);
		}

		public int? ToRawInt()
		{
			if (string.IsNullOrWhiteSpace(Text))
			{
				if (Nullable)
				{
					return null;
				}
				
				return 0;
			}

			return int.Parse(Text, NumberStyles.HexNumber);
		}

		public void SetFromRawInt(int? val)
		{
			Text = val.HasValue ? string.Format(_addressFormatStr, val) : string.Empty;
		}
	}

	public class UnsignedIntegerBox : TextBox, INumberBox
	{
		private bool _nullable = true;

		public UnsignedIntegerBox()
		{
			CharacterCasing = CharacterCasing.Upper;
		}

		public bool Nullable { get { return _nullable; } set { _nullable = value; } }

		protected override void OnKeyPress(KeyPressEventArgs e)
		{
			if (e.KeyChar == '\b' || e.KeyChar == 22 || e.KeyChar == 1 || e.KeyChar == 3)
			{
				return;
			}
			
			if (!InputValidate.IsUnsigned(e.KeyChar))
			{
				e.Handled = true;
			}
		}

		public override void ResetText()
		{
			Text = _nullable ? string.Empty : "0";
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Up)
			{
				if (InputValidate.IsUnsigned(Text))
				{
					var val = (uint)ToRawInt();
					if (val == uint.MaxValue)
					{
						val = 0;
					}
					else
					{
						val++;
					}

					Text = val.ToString();
				}
			}
			else if (e.KeyCode == Keys.Down)
			{
				if (InputValidate.IsUnsigned(Text))
				{
					var val = (uint)ToRawInt();

					if (val == 0)
					{
						val = uint.MaxValue;
					}
					else
					{
						val--;
					}

					Text = val.ToString();
				}
			}
			else
			{
				base.OnKeyDown(e);
			}
		}

		protected override void OnTextChanged(EventArgs e)
		{
			if (string.IsNullOrWhiteSpace(Text))
			{
				ResetText();
			}

			base.OnTextChanged(e);
		}

		public int? ToRawInt()
		{
			if (string.IsNullOrWhiteSpace(Text))
			{
				if (Nullable)
				{
					return null;
				}
				
				return 0;
			}

			return (int)uint.Parse(Text);
		}

		public void SetFromRawInt(int? val)
		{
			Text = val.HasValue ? val.ToString() : string.Empty;
		}
	}
}
