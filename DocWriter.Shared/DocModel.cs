﻿//
// DocModel.cs: Contains the various nodes that know how to render and edit pages
//
// Author:
//   Miguel de Icaza (miguel@xamarin.com)
//
// Copyright 2014 Xamarin Inc
//
using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Xml;
using System.Text;
using System.Web;

#if __XAMARIN_MAC__
using NSObject = MonoMac.Foundation.NSObject;
#else
using NSObject = System.Object;
#endif

namespace DocWriter
{
	// Interface implemented by nodes that can render themselves as HTML
	// typically this renders the editable content as well.
	interface IHtmlRender {
		string Render ();
	}

	// Interface implemented to lookup the contents of a node
	public interface IWebView {
		string RunJS (string functionName, params string[] args);
	}
	
	// Interface implemented to handle operations
	public interface IEditorWindow : IWebView {
		// the path and model for these docs
		string WindowPath { get; }
		DocModel DocModel { get; }

		// the currently editing object
		DocNode CurrentObject { get; set; }

		// update the editor
		void UpdateStatus (string status);
	}

	public enum SelectionToCodeType {
		ParamRef,
		TypeParamRef,
		LangWord
	}

	// Extensions to add features and operations
	public static class Extensions {

		// extensions

		public static string EscapeHtml (string html) {
			var sb = new StringBuilder ();
			foreach (char c in html) {
				if (c == '\n')
					sb.Append ("\\\n");
				else
					sb.Append (c);
			}
			return sb.ToString ();
		}
		
		// IWebView extensions

		public static string Fetch (this IWebView webView, string id) {
			var element = webView.RunJS ("getHtml", id);
			if (element.StartsWith ("<<<<", StringComparison.OrdinalIgnoreCase)) {
				Console.WriteLine ("Failure to fetch contents of {0}", id);
			}
			return element;
		}

		public static void InsertSpan (this IWebView webView, string html) {
			webView.RunJS ("insertSpanAtCursor", EscapeHtml (html));
		}
		
		public static void InsertHtml (this IWebView webView, string html, params object[] args) {
			webView.InsertSpan (string.Format (html, args));
		}
		
		public static void InsertUrl (this IWebView webView, string caption, string url)
		{
			webView.InsertHtml (string.Format ("<div class='verbatim'><a href='{0}'>{1}</a></div>", url, caption));
		}

		public static void SelectionToCode (this IWebView webView, SelectionToCodeType type) {
			string codeType = null;
			switch (type) {
				case SelectionToCodeType.LangWord:
					codeType = "langword";
					break;
				case SelectionToCodeType.ParamRef:
					codeType = "paramref";
					break;
				case SelectionToCodeType.TypeParamRef:
					codeType = "typeparamref";
					break;
			}
			if (codeType != null) {
				webView.RunJS ($"selectionToCode", codeType);
			}
		}

		public static void InsertReference (this IWebView webView, DocNode docNode, string text = null) {
			webView.InsertHtml ("<a href=''>T:" + docNode.SuggestTypeRef () + "</a>");
		}
		
		public static void InsertImage (this IWebView webView, string target) {
			webView.InsertHtml("<img src='{0}'>", target);
		}

		// IEditorWindow extensions
		
		public static void SaveCurrentObject (this IEditorWindow editorWindow) {
			var editable = editorWindow.CurrentObject as IEditableNode;
			if (editable != null) {
				string error;

				editorWindow.CheckContents ();

				if (!editable.Save (editorWindow, out error)) {
					// FIXME: popup a window or something.
				}
			}
		}

		public static void InsertTable (this IEditorWindow editorWindow)
		{
			var table = new XElement ("list", new XAttribute ("type", "table"),
			                          new XElement ("listheader", 
			                                        new XElement ("term", new XText ("Term")),
			                                        new XElement ("description", new XText ("Description"))),
			                          new XElement ("item", 
			                                        new XElement ("term", new XText ("Term1")),
			                                        new XElement ("description", new XText ("Description1"))),
			                          new XElement ("item", 
			                                        new XElement ("term", new XText ("Term2")),
			                                        new XElement ("description", new XText ("Description2"))));
			
			editorWindow.AppendEcmaNode (new XElement ("Host", table));
		}
		
		public static void InsertReference (this IEditorWindow editorWindow, string text = null) {
			editorWindow.InsertReference (editorWindow.CurrentObject, text);
		}

		public static void InsertList (this IEditorWindow editorWindow) {
			var list = new XElement ("list", new XAttribute ("type", "bullet"),
				new XElement ("item", new XElement ("term", new XText ("Text1"))),
				new XElement ("item", new XElement ("term", new XText ("Text2"))));

			editorWindow.AppendEcmaNode (new XElement ("host", list));
		}
		
		public static void InsertHtmlH2 (this IEditorWindow editorWindow) {
			editorWindow.AppendEcmaNode (new XElement ("host", new XElement ("format", new XAttribute ("type", "text/html"), new XElement ("h2", new XText ("Header")))));
		}
		
		public static void InsertCSharpCode (this IEditorWindow editorWindow) {
			var example = new XElement ("host", new XElement ("code", new XAttribute ("lang", "C#"), new XCData ("class Sample {")));

			editorWindow.AppendEcmaNode (example);
			editorWindow.AppendPara ();
		}

		public static void InsertCSharpExample (this IEditorWindow editorWindow) {
			var example = new XElement ("host", new XElement ("example", new XElement ("code", new XAttribute ("lang", "C#"), new XCData ("class Sample {"))));

			editorWindow.AppendEcmaNode (example);
			editorWindow.AppendPara ();
		}

		public static void InsertFSharpCode (this IEditorWindow editorWindow) {
			var example = new XElement ("host", new XElement ("code", new XAttribute ("lang", "F#"), new XCData ("let sample = ")));

			editorWindow.AppendEcmaNode (example);
			editorWindow.AppendPara ();
		}

		public static void InsertFSharpExample (this IEditorWindow editorWindow) {
			var example = new XElement ("host", new XElement ("example", new XElement ("code", new XAttribute ("lang", "F#"), new XCData ("let sample = "))));

			editorWindow.AppendEcmaNode (example);
			editorWindow.AppendPara ();
		}

		public static void AppendPara (this IEditorWindow editorWindow) {
			editorWindow.AppendEcmaNode (new XElement ("para", new XText (".")));
		}
		
		// Turns the provided ECMA XML node into HTML and appends it to the current node on the rendered HTML
		public static void AppendEcmaNode (this IEditorWindow editorWindow, XElement ecmaXml) {
			if (editorWindow.CurrentObject != null) {
				var html = DocConverter.ToHtml (ecmaXml, "", editorWindow.CurrentObject.DocumentDirectory);
				editorWindow.RunJS ("insertHtmlAfterCurrentNode", EscapeHtml (html));
			}
		}
		
		public static void CheckContents (this IEditorWindow editorWindow)
		{
			var dirtyNodes = editorWindow.RunJS ("getDirtyNodes").Split (new char [] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (dirtyNodes.Length == 0)
				return;

			if (editorWindow.CurrentObject != null && editorWindow.CurrentObject is IEditableNode) {
				string result;
				try {
					result = (editorWindow.CurrentObject as IEditableNode).ValidateChanges (editorWindow, dirtyNodes);
				} catch (Exception e) {
					result = e.ToString ();
				}

				editorWindow.UpdateStatus (result ?? "OK");
			}
		}

	}
	

	// Interface implemented by nodes that can provide editing functionality
	interface IEditableNode {
		string ValidateChanges (IWebView webView, string [] nodes);
		bool Save (IWebView webView, out string error);
	}

	// It is an NSObject, because we conveniently stash these in the NSOutlineView
	public class DocNode : NSObject {
		public string CName;

		string _name;
		public string Name { 
			get { return _name; }
			set {
				_name = value;
				CName = value.ToString ();
			}
		}

		public string GetHtmlForNode (XElement element, string xpath)
		{
			return DocConverter.ToHtml (element.XPathSelectElement (xpath), Name.ToString (), DocumentDirectory);
		}
			
		//
		// Updates the target XElement with the edited text.   
		// The first pass uses the provided xpath expression to remove all the nodes that match
		// this expression from the target (this cleans the nodes that we are about to replace)
		// then the named htmlElement is used to lookup the contents on WebKit and the results
		// are converted back to XML which is stashed in the target specified by xpath.
		//
		// Returns true on success
		public bool UpdateNode (IWebView webView, XElement target, string xpath, string htmlElement, out string error)
		{
			error = null;
			var node = target.XPathSelectElement (xpath);
			node.RemoveNodes ();

			try {
				var str = webView.Fetch (htmlElement);
				foreach (var ret in DocConverter.ToXml (str, canonical: true))
					node.Add (ret);
			} catch (UnsupportedElementException e){
				error = e.Message;
				return false;
			}
			return true;
		}

		public static bool SaveXDocument (XDocument doc, string path, out string error)
		{
			error = null;
			try {
				var s = new XmlWriterSettings () {
					Indent = true,
					Encoding = new UTF8Encoding (false),
					OmitXmlDeclaration = true,
					NewLineChars = Environment.NewLine
				};
				using (var stream = File.Create (path)){
					using (var xmlw = XmlWriter.Create (stream, s)){
						doc.Save (xmlw);
					}
					stream.Write (new byte [] { 10 }, 0, 1);
				} 
				return true;
			} catch (Exception e){
				error = e.ToString ();
				return false;
			}
		}

		// Validates that the elements named in 'args' in WebKit can be converted
		// from HTML back into ECMA XML.
		//
		// Returns null on success, otherwise a string with the error details
		//
		public string ValidateElements (IWebView web, params string [] args)
		{
			foreach (var arg in args) {
				try {
					var str = web.Fetch (arg);
					DocConverter.ToXml (str, canonical: true);
					web.RunJS ("postOk('" + arg + "')");
				} catch (UnsupportedElementException e){
					web.RunJS ("postError('" + arg + "')");
					return "Parsing error: " + e.Message;
				} catch (StackOverflowException e){
					return "Exception " + e.GetType ().ToString ();
				}
			}
			return null;
		}

		// Returns this node path, useful on the IDe to remember which node to reload
		public virtual string DocPath {
			get {
				return "";
			}
		}

		public virtual string DocumentDirectory {
			get {
				return "";

			}
		}

		public virtual bool IsAutodocumented {
			get {
				return false;
			}
		}

		public string ToHtml (XElement root)
		{
			return DocConverter.ToHtml (root, "inmemory", DocumentDirectory);
		}

		// Suggests a name for the reference based on the current context
		public string SuggestTypeRef() {
			var cns = this as DocNamespace;
			if (cns != null)
				return cns.Name;
			var ctype = this as DocType;
			if (ctype != null)
				return ctype.Namespace.Name + "." + ctype.Name;
			var cm = this as DocMember;
			if (cm != null)
				return cm.Type.Namespace.Name + "." + cm.Type.Name;
			return "MonoTouch.";
		}

	}

	// DocMember: renders an ECMA type member (methods, properties, properties, fields)
	public class DocMember : DocNode, IHtmlRender, IEditableNode {
		public DocType Type { get; private set; }
		public XElement MemberElement;
		public IEnumerable<XElement> MemberParams;
		public IEnumerable<XElement> Params;
		public XElement Value;
		public XElement ReturnValue;
		public XElement Remarks;
		public string Kind;

		public DocMember (DocType type, XElement e)
		{
			this.Type = type;
			this.MemberElement = e;
			Name = e.Attribute ("MemberName").Value;
			Remarks = e.XPathSelectElement ("Docs/remarks");
			MemberParams = e.XPathSelectElements("Parameters/Parameter");
			Params = e.XPathSelectElements ("Docs/param");
			Value = e.XPathSelectElement ("Docs/value");
			ReturnValue = e.XPathSelectElement ("Docs/returns");
			Kind = e.XPathSelectElement ("MemberType").Value;
		}

		public string Render ()
		{
			var ret = new MemberTemplate () { Model = this }.GenerateString ();
			return ret;
		}

		public string SummaryHtml {
			get {
				return GetHtmlForNode (MemberElement, "Docs/summary");
			}
		}

		public string SignatureHtml {
			get {
				return HttpUtility.HtmlEncode (MemberElement.XPathSelectElement ("MemberSignature").Attribute ("Value").Value);
			}
		}

		public string GetHtmlForNode (string xpath)
		{
			return GetHtmlForNode (MemberElement, xpath);
		}
			
		public string ValidateChanges (IWebView webView, string [] nodes)
		{
			var err = ValidateElements (webView, nodes);
			if (err != null)
				return err;
			return null;
		}

		public bool Save (IWebView webView, out string error)
		{
			error = null;
			if (!UpdateNode (webView, MemberElement, "Docs/summary", "summary", out error))
				return false;
			if (Remarks != null && !UpdateNode (webView, MemberElement, "Docs/remarks", "remarks", out error))
				return false;
			if (Value != null && !UpdateNode (webView, MemberElement, "Docs/value", "value", out error))
				return false;
			if (ReturnValue != null && !UpdateNode (webView, MemberElement, "Docs/returns", "return", out error))
				return false;
			if (Params != null) {
				foreach (var p in Params) {
					string name = "param-" + p.Attribute ("name").Value;
					if (!UpdateNode (webView, p, ".", name, out error))
						return false;
				}
			}
			return Type.SaveDoc (out error);
		}

		public override string DocPath {
			get {
				return Type.DocPath + "/" + CName;
			}
		}

		public override string DocumentDirectory {
			get {
				return Type.DocumentDirectory;
			}
		}

		private bool IsAutodocumentedConstructor()
		{
			if (CName == ".ctor") {
				var argCount = Enumerable.Count (MemberElement.XPathSelectElements ("Parameters/Parameter"));
				switch (argCount) {
				case 0: //Default .ctor
					return true;
				case 1:
					//IntPtr
					if (Enumerable.Count (MemberElement.XPathSelectElements ("Parameters/Parameter[@Type='System.IntPtr']")) == 1) {
						return true;
					}
					//NSCoder
					if (Enumerable.Count (MemberElement.XPathSelectElements ("Parameters/Parameter[@Type='MonoTouch.Foundation.NSCoder']")) == 1) {
						return true;
					}
					//NSObjectFlag
					if (Enumerable.Count (MemberElement.XPathSelectElements ("Parameters/Parameter[@Type='MonoTouch.Foundation.NSObjectFlag']")) == 1) {
						return true;
					}
					return false;
				default : 
					return false;
				}
			}
			return false;
		}

		private bool IsClassHandle ()
		{
			return CName == "ClassHandle";;
		}

		private bool IsAutodocumentedAppearance ()
		{
			return CName == "Appearance" | CName == "AppearanceWhenContainedIn";
		}

		private bool IsNotificationProperty ()
		{
			return 
				CName.EndsWith("Notification") 
				&& MemberElement.XPathSelectElement("ReturnValue/ReturnType").Value == "MonoTouch.Foundation.NSString";
		}

		private bool IsDelegateProperty ()
		{
			return CName == "Delegate";
		}

		private bool IsWeakDelegateProperty ()
		{
			return CName == "WeakDelegate";
		}

		private bool IsDisposeOrFinalizer ()
		{
			//TODO: Confirm Finalizer name
			return CName == "Dispose" | CName == "Finalize"; 
		}

		public override bool IsAutodocumented {
			get {
				return
				IsAutodocumentedConstructor ()
				| IsClassHandle ()
				| IsAutodocumentedAppearance ()
				| IsNotificationProperty ()
				| IsDelegateProperty ()
				| IsWeakDelegateProperty ()
				| IsDisposeOrFinalizer ();
			}
		}
	}

	// DocType: renders an ECMA type
	public class DocType : DocNode, IHtmlRender, IEditableNode {
		public DocNamespace Namespace { get; private set; }
		public IEnumerable<XElement> Params;
		public XElement Root;

		// Keeps track of altered nodes in the summaries
		Dictionary<string,string> dirtyNodes = new Dictionary<string, string>();

		// The contents of the XML document, as read from disk
		XDocument doc;
		XElement [] xml_members;
		DocMember [] members;
		string path;

		public DocType (DocNamespace ns, string path)
		{
			this.path = path;
			Namespace = ns;
			Name = Path.GetFileNameWithoutExtension (path);
			try {
				doc = XDocument.Load (path);
				Root = doc.Root;
				xml_members = doc.XPathSelectElements ("/Type/Members/Member").ToArray ();
				Params = doc.XPathSelectElements ("/Type/Docs/param");
				members = new DocMember[xml_members.Length];
			} catch {
			}
		}

		public string FullName {
			get {
				return Namespace.Name + "." + Name;
			}
		}

		public int NodeCount {
			get {
				return xml_members.Length;
			}
		}

		public DocMember this [int idx]{
			get {
				if (members [idx] == null) 
					members [idx] = new DocMember (this, xml_members [idx]);
				return members [idx];
			}
		}

		public string Render ()
		{
			var ret = new TypeTemplate () { Model = this }.GenerateString ();
			return ret;
		}

		public string KindHtml {
			get {
				var x = doc.XPathSelectElement ("/Type/Base/BaseTypeName");
				if (x == null)
					return "interface";

				switch (x.Value) {
				case "System.Enum":
					return "enum";
				case "System.ValueType":
					return "struct";
				case "System.Delegate":
					return "delegate";
				default:
					return "class";
				}
			}
		}

		public string ToBeAddedClass {
			get {
				if (Root.XPathSelectElement ("/Type/Docs/summary").Value == "To be added.")
					return "to-be-added";
				return "";
			}
		}

		public string SummaryHtml {
			get {
				return GetHtmlForNode (Root, "/Type/Docs/summary");
			}
		}

		public string RemarksHtml {
			get {
				var x =  GetHtmlForNode (Root, "/Type/Docs/remarks");
				return x;
			}
		}

		public string ValidateChanges (IWebView webView, string [] nodes)
		{
			var ret = ValidateElements (webView, nodes);
			if (ret != null)
				return ret;
			foreach (var n in nodes) {
				if (dirtyNodes.ContainsKey (n))
					continue;
				dirtyNodes [n] = n;
			}
			return null;
		}

		public bool SaveDoc (out string error)
		{
			return SaveXDocument (doc, path, out error);
		}

		public bool Save (IWebView webView, out string error)
		{
			error = null;
			if (UpdateNode (webView, Root, "/Type/Docs/summary", "summary", out error) &&
			    UpdateNode (webView, Root, "/Type/Docs/remarks", "remarks", out error)) {

				foreach (var dirty in dirtyNodes.Keys) {
					if (dirty.StartsWith ("summary-")) {
						int idx;
						if (Int32.TryParse (dirty.Substring (8), out idx)) {
							var node = this [idx];
							if (!node.UpdateNode (webView, this [idx].MemberElement, "Docs/summary", dirty, out error))
								return false;
						} else {
							throw new Exception ("You introduced a new summary-XX id that we can not save");
						}
					}
				}
				if (Params != null) {
					foreach (var p in Params) {
						string name = "param-" + p.Attribute ("name").Value;
						if (!UpdateNode (webView, p, ".", name, out error))
							return false;
					}
				}

				dirtyNodes.Clear ();

				if (SaveDoc (out error))
					return true;
				error = "Error while saving the XML file to " + path;
			}

			return false;
		}
		public override string DocPath {
			get {
				return Namespace.DocPath + "/" + CName;
			}
		}

		public override string DocumentDirectory {
			get {
				return Namespace.DocumentDirectory;
			}
		}
	}

	// Provides a host to show the namespace on the tree.
	public class DocNamespace : DocNode, IHtmlRender, IEditableNode {
		Dictionary<string,string> dirtyNodes = new Dictionary<string, string>();
		SortedList<string,DocType> docs = new SortedList<string, DocType> ();
		XDocument nsDoc;
		XElement root;
		string path, nsPath;

		public DocNamespace (string path)
		{
			this.path = path;
			Name = Path.GetFileName (path);
			foreach (var file in Directory.GetFiles (path, "*.xml")){
				docs [Path.GetFileName (file)] = null;
			}
			nsPath = Path.Combine (Path.GetDirectoryName (path), "ns-" + Path.GetFileName (path) + ".xml");
			if (File.Exists (nsPath)) {
				Console.WriteLine ("Loading {0}", nsPath);
				try {
					nsDoc = XDocument.Load (nsPath);
					root = nsDoc.Root;
				} catch (Exception e) {
					Console.WriteLine ("Failed to load " + nsPath + e);
					nsDoc = null;
				}
			}
		}
		public int NodeCount {
			get {
				return docs.Count;
			}
		}

		public DocType this [int idx] {
			get {
				if (docs.Values [idx] == null) {
					var key = docs.Keys [idx];
					docs [key] = new DocType (this, Path.Combine (path, key));
				}
				return docs.Values [idx];
			}
		}

		public string Render ()
		{
			var ret = new NamespaceTemplate () { Model = this }.GenerateString ();
			return ret;
		}

		public string ValidateChanges (IWebView webView, string[] nodes)
		{			
			var ret = ValidateElements (webView, nodes);
			if (ret != null)
				return ret;
			foreach (var n in nodes) {
				if (dirtyNodes.ContainsKey (n))
					continue;
				dirtyNodes [n] = n;
			}
			return null;
		}

		public bool Save (IWebView webView, out string error)
		{
			error = null;
			foreach (var dirty in dirtyNodes.Keys) {
				if (dirty.StartsWith ("summary-")) {
					int idx;
					if (Int32.TryParse (dirty.Substring (8), out idx)) {
						var node = this [idx];
						if (!node.UpdateNode (webView, node.Root,"/Type/Docs/summary", dirty, out error))
							return false;
						Console.WriteLine ("SAVING NODE {0}", node.Name);
						if (!node.SaveDoc (out error))
							return false;
					} else {
						throw new Exception ("You introduced a new summary-XX id that we can not save");
					}
				}
			}
			if (nsDoc != null) {
				bool dirtyNS = false;
				if (dirtyNodes.ContainsKey ("summary")) {
					dirtyNS = true;
					if (!UpdateNode (webView, root, "/Namespace/Docs/summary", "summary", out error))
						return false;
				}
				if (dirtyNodes.ContainsKey ("remarks")) {
					dirtyNS = true;
					if (!UpdateNode (webView, root, "/Namespace/Docs/remarks", "remarks", out error))
						return false;
				}
				if (dirtyNS) {
					if (!SaveXDocument (nsDoc, nsPath, out error))
						return false;
				}
			}
			dirtyNodes.Clear ();
			return true;
		}

		public override string DocPath {
			get {
				return CName;
			}
		}

		public override string DocumentDirectory {
			get {
				return path;
			}
		}

		public string SummaryHtml {
			get {
				if (root == null)
					return "Missing namespace documentation file";
				return GetHtmlForNode (root, "/Namespace/Docs/summary");
			}
		}

		public string RemarksHtml {
			get {
				if (root == null)
					return "Missing namespace documentation file";
				var x =  GetHtmlForNode (root, "/Namespace/Docs/remarks");
				return x;
			}
		}

	}

	// Loads the documentation from disk, on demand.
	public class DocModel
	{
		List<DocNamespace> namespaces = new List<DocNamespace> ();

		public DocModel (string path)
		{
			var dirs = 
				from p in Directory.GetDirectories (path)
				orderby p
					where Path.GetFileName (p) != "Mono.Options"
				select p;

			foreach (var dir in dirs) {
				namespaces.Add (new DocNamespace (dir));
			}
		}

		public int NodeCount {
			get {
				return namespaces.Count;
			}
		}

		public DocNamespace this [int idx] {
			get {
				return namespaces [idx];
			}
		}


	}
}
