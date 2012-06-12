/*
*
*   Copyright (c) 2010, Raymond Glover
*   All rights reserved.
*
*   Redistribution and use in source and binary forms, with or without modification, 
*   are permitted provided that the following conditions are met:
*
*     1. Redistributions of source code must retain the above copyright notice, 
*        this list of conditions and the following disclaimer.
*     2. Redistributions in binary form must reproduce the above copyright 
*        notice, this list of conditions and the following disclaimer in the 
*        documentation and/or other materials provided with the distribution.
*     3. Neither the name of Cynosura nor the names of its contributors may 
*        be used to endorse or promote products derived from this software 
*        without specific prior written permission.
*
*   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY
*   EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES 
*   OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT 
*   SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, 
*   INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED 
*   TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; 
*   OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
*   CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN 
*   ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH 
*   DAMAGE.
*
*/

namespace ElmcityUtils
{
	using System;
	using System.IO;
	using System.Text;
	using System.Collections.Generic;
	using System.Web.Script.Serialization;

	/// <summary>
	///   A helper class for generating object graphs in 
	///   JavaScript notation. Supports pretty printing.
	/// </summary>
	public class JsonHelper
	{
		public static string Serialize(Object target)
		{
			return Serialize(target, false);
		}

		public static string Serialize(Object target, bool prettyPrint)
		{
			StringBuilder sbSerialized = new StringBuilder();
			JavaScriptSerializer js = new JavaScriptSerializer();

			js.Serialize(target, sbSerialized);

			if (prettyPrint)
			{
				StringBuilder prettyPrintedResult = new StringBuilder();
				prettyPrintedResult.EnsureCapacity(sbSerialized.Length);

				JsonPrettyPrinter pp = new JsonPrettyPrinter();
				pp.PrettyPrint(sbSerialized, prettyPrintedResult);

				return prettyPrintedResult.ToString();
			}
			else
				return sbSerialized.ToString();
		}
	}

	public enum JsonContextType
	{
		Object, Array
	}

	public class JsonPrettyPrinter
	{
		#region class members
		const string Space = " ";
		const int DefaultIndent = 0;
		const string Indent = Space + Space + Space + Space;
		static readonly string NewLine = Environment.NewLine;

		static void BuildIndents(int indents, StringBuilder output)
		{
			indents += DefaultIndent;
			for (; indents > 0; indents--)
				output.Append(Indent);
		}
		#endregion

		bool inDoubleString = false;
		bool inSingleString = false;
		bool inVariableAssignment = false;
		char prevChar = '\0';

		Stack<JsonContextType> context = new Stack<JsonContextType>();

		bool InString()
		{
			return inDoubleString || inSingleString;
		}

		public void PrettyPrint(StringBuilder input, StringBuilder output)
		{
			if (input == null) throw new ArgumentNullException("input");
			if (output == null) throw new ArgumentNullException("output");

			int inputLength = input.Length;
			char c;

			for (int i = 0; i < inputLength; i++)
			{
				c = input[i];

				switch (c)
				{
					case '{':
						if (!InString())
						{
							if (inVariableAssignment || (context.Count > 0 && context.Peek() != JsonContextType.Array))
							{
								output.Append(NewLine);
								BuildIndents(context.Count, output);
							}
							output.Append(c);
							context.Push(JsonContextType.Object);
							output.Append(NewLine);
							BuildIndents(context.Count, output);
						}
						else
							output.Append(c);

						break;

					case '}':
						if (!InString())
						{
							output.Append(NewLine);
							context.Pop();
							BuildIndents(context.Count, output);
							output.Append(c);
						}
						else
							output.Append(c);

						break;

					case '[':
						output.Append(c);

						if (!InString())
							context.Push(JsonContextType.Array);

						break;

					case ']':
						if (!InString())
						{
							output.Append(c);
							context.Pop();
						}
						else
							output.Append(c);

						break;

					case '=':
						output.Append(c);
						break;

					case ',':
						output.Append(c);

						if (!InString() && context.Peek() != JsonContextType.Array)
						{
							BuildIndents(context.Count, output);
							output.Append(NewLine);
							BuildIndents(context.Count, output);
							inVariableAssignment = false;
						}

						break;

					case '\'':
						if (!inDoubleString && prevChar != '\\')
							inSingleString = !inSingleString;

						output.Append(c);
						break;

					case ':':
						if (!InString())
						{
							inVariableAssignment = true;
							output.Append(Space);
							output.Append(c);
							output.Append(Space);
						}
						else
							output.Append(c);

						break;

					case '"':
						if (!inSingleString && prevChar != '\\')
							inDoubleString = !inDoubleString;

						output.Append(c);
						break;

					default:
						output.Append(c);
						break;
				}
				prevChar = c;
			}
		}
	}
}