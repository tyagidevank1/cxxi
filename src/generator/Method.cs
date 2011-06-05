//
// Method.cs: Represents a C++ method
//
// Author:
//   Alexander Corrado (alexander.corrado@gmail.com)
//   Andreia Gaita (shana@spoiledcat.net)
//   Zoltan Varga <vargaz@gmail.com>
//
// Copyright (C) 2011 Novell Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Linq;
using System.Collections.Generic;
using System.CodeDom;
using System.CodeDom.Compiler;

using Mono.VisualC.Interop;

class Method
{
	public Method (Node node) {
		Node = node;
	    Parameters = new List<Parameter> ();
		GenWrapperMethod = true;
	}

	public Node Node {
		get; set;
	}

	public string Name {
		get; set;
	}

	public bool IsVirtual {
		get; set;
	}

	public bool IsStatic {
		get; set;
	}

	public bool IsConst {
		get; set;
	}

	public bool IsInline {
		get; set;
	}

	public bool IsArtificial {
		get; set;
	}

	public bool IsConstructor {
		get; set;
	}

	public bool IsDestructor {
		get; set;
	}

	public bool IsCopyCtor {
		get; set;
	}

	public bool GenWrapperMethod {
		get; set;
	}

	public CppType ReturnType {
		get; set;
	}

	public List<Parameter> Parameters {
		get; set;
	}

	// The C# method name
	public string FormattedName {
		get {
			return "" + Char.ToUpper (Name [0]) + Name.Substring (1);
		}
	}

	string GetCSharpMethodName (string name) {
		return "" + Char.ToUpper (name [0]) + name.Substring (1);
	}

	public CodeMemberMethod GenerateIFaceMethod (Generator g) {
		var method = new CodeMemberMethod () {
				Name = Name
		};

		if (!IsStatic)
			method.Parameters.Add (new CodeParameterDeclarationExpression (new CodeTypeReference ("CppInstancePtr"), "this"));

		CodeTypeReference rtype = g.CppTypeToCodeDomType (ReturnType);
		method.ReturnType = rtype;
		if ((ReturnType.ElementType == CppTypes.Class || ReturnType.ElementType == CppTypes.Struct) &&
			!ReturnType.Modifiers.Contains (CppModifiers.Pointer) &&
			!ReturnType.Modifiers.Contains (CppModifiers.Reference) &&
			!ReturnType.Modifiers.Contains (CppModifiers.Array))
		{
			method.ReturnTypeCustomAttributes.Add (new CodeAttributeDeclaration ("ByVal"));
		}

		foreach (var p in Parameters) {
			CppType ptype = p.Type;
			bool byref;
			var ctype = g.CppTypeToCodeDomType (ptype, out byref);
			var param = new CodeParameterDeclarationExpression (ctype, p.Name);
			if (byref)
				param.Direction = FieldDirection.Ref;
			if (!IsVirtual && !ptype.ToString ().Equals (string.Empty))
				param.CustomAttributes.Add (new CodeAttributeDeclaration ("MangleAsAttribute", new CodeAttributeArgument (new CodePrimitiveExpression (ptype.ToString ()))));
			if ((ptype.ElementType == CppTypes.Class || ptype.ElementType == CppTypes.Struct) &&
				!ptype.Modifiers.Contains (CppModifiers.Pointer) &&
				!ptype.Modifiers.Contains (CppModifiers.Reference) &&
				!ptype.Modifiers.Contains (CppModifiers.Array))
			{
				param.CustomAttributes.Add (new CodeAttributeDeclaration ("ByVal"));
			}
			method.Parameters.Add (param);
		}

		// FIXME: Copy ctor

		if (IsVirtual)
			method.CustomAttributes.Add (new CodeAttributeDeclaration ("Virtual"));
		if (IsConstructor)
			method.CustomAttributes.Add (new CodeAttributeDeclaration ("Constructor"));
		if (IsDestructor)
			method.CustomAttributes.Add (new CodeAttributeDeclaration ("Destructor"));
		if (IsConst)
			method.CustomAttributes.Add (new CodeAttributeDeclaration ("Const"));
		if (IsInline)
			method.CustomAttributes.Add (new CodeAttributeDeclaration ("Inline"));
		if (IsArtificial)
			method.CustomAttributes.Add (new CodeAttributeDeclaration ("Artificial"));
		if (IsCopyCtor)
			method.CustomAttributes.Add (new CodeAttributeDeclaration ("CopyConstructor"));
		if (IsStatic)
			method.CustomAttributes.Add (new CodeAttributeDeclaration ("Static"));

		return method;
	}

	public CodeMemberMethod GenerateWrapperDeclaration (Generator g) {
		CodeMemberMethod method;

		if (IsConstructor)
			method = new CodeConstructor () {
				Name = GetCSharpMethodName (Name)
			};
		else
			method = new CodeMemberMethod () {
				Name = GetCSharpMethodName (Name)
			};
		method.Attributes = MemberAttributes.Public;
		if (IsStatic)
			method.Attributes |= MemberAttributes.Static;

		method.ReturnType = g.CppTypeToCodeDomType (ReturnType);

		foreach (var p in Parameters) {
			bool byref;
			var ptype = g.CppTypeToCodeDomType (p.Type, out byref);
			var param = new CodeParameterDeclarationExpression (ptype, p.Name);
			if (byref)
				param.Direction = FieldDirection.Ref;
			method.Parameters.Add (param);
		}

		return method;
	}

	IEnumerable<CodeExpression> GetArgumentExpressions (Generator g) {
		for (int i = 0; i < Parameters.Count; ++i) {
			bool byref;
			g.CppTypeToCodeDomType (Parameters [i].Type, out byref);
			CodeExpression arg = new CodeArgumentReferenceExpression (Parameters [i].Name);
			if (byref)
				arg = new CodeDirectionExpression (FieldDirection.Ref, arg);
			yield return arg;
		}
		yield break;
	}

	// for methods inherited from non-primary bases
	public CodeMemberMethod GenerateInheritedWrapperMethod (Generator g, Class baseClass) {
		var method = GenerateWrapperDeclaration (g);
		var args = GetArgumentExpressions (g).ToArray ();

		var call = new CodeMethodInvokeExpression (new CodeCastExpression (baseClass.Name, new CodeThisReferenceExpression ()), method.Name, args);

		if (method.ReturnType.BaseType == "System.Void")
			method.Statements.Add (call);
		else
			method.Statements.Add (new CodeMethodReturnStatement (call));

		return method;
	}

	public CodeMemberMethod GenerateWrapperMethod (Generator g) {
		var method = GenerateWrapperDeclaration (g);

		if (IsConstructor) {
            //this.native_ptr = impl.Alloc(this);
			method.Statements.Add (new CodeAssignStatement (new CodeFieldReferenceExpression (null, "native_ptr"), new CodeMethodInvokeExpression (new CodeMethodReferenceExpression (new CodeFieldReferenceExpression (null, "impl"), "Alloc"), new CodeExpression [] { new CodeThisReferenceExpression () })));
		}

		// Call the iface method
		var args = new CodeExpression [Parameters.Count + (IsStatic ? 0 : 1)];
		if (!IsStatic)
			args [0] = new CodeFieldReferenceExpression (null, "Native");

		var i = 0;
		foreach (var arg in GetArgumentExpressions (g)) {
			args [i + (IsStatic ? 0 : 1)] = arg;
			i++;
		}

		var call = new CodeMethodInvokeExpression (new CodeMethodReferenceExpression (new CodeFieldReferenceExpression (null, "impl"), Name), args);

		if (method.ReturnType.BaseType == "System.Void" || IsConstructor)
			method.Statements.Add (call);
		else
			method.Statements.Add (new CodeMethodReturnStatement (call));

		return method;
	}
}
