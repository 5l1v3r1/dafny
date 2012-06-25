
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics.Contracts;
using Microsoft.Boogie;


namespace Microsoft.Dafny {

//-----------------------------------------------------------------------------------
// Buffer
//-----------------------------------------------------------------------------------
public class Buffer {
	// This Buffer supports the following cases:
	// 1) seekable stream (file)
	//    a) whole stream in buffer
	//    b) part of stream in buffer
	// 2) non seekable stream (network, console)

	public const int EOF = 65535 + 1; // char.MaxValue + 1;
	const int MIN_BUFFER_LENGTH = 1024; // 1KB
	const int MAX_BUFFER_LENGTH = MIN_BUFFER_LENGTH * 64; // 64KB
	byte[]/*!*/ buf;         // input buffer
	int bufStart;       // position of first byte in buffer relative to input stream
	int bufLen;         // length of buffer
	int fileLen;        // length of input stream (may change if the stream is no file)
	int bufPos;         // current position in buffer
	Stream/*!*/ stream;      // input stream (seekable)
	bool isUserStream;  // was the stream opened by the user?

	[ContractInvariantMethod]
	void ObjectInvariant(){
		Contract.Invariant(buf != null);
		Contract.Invariant(stream != null);
	}

//  [NotDelayed]
	public Buffer (Stream/*!*/ s, bool isUserStream) : base() {
	  Contract.Requires(s != null);
		stream = s; this.isUserStream = isUserStream;

		int fl, bl;
		if (s.CanSeek) {
			fl = (int) s.Length;
			bl = fl < MAX_BUFFER_LENGTH ? fl : MAX_BUFFER_LENGTH; // Math.Min(fileLen, MAX_BUFFER_LENGTH);
			bufStart = Int32.MaxValue; // nothing in the buffer so far
		} else {
			fl = bl = bufStart = 0;
		}

		buf = new byte[(bl>0) ? bl : MIN_BUFFER_LENGTH];
		fileLen = fl;  bufLen = bl;

		if (fileLen > 0) Pos = 0; // setup buffer to position 0 (start)
		else bufPos = 0; // index 0 is already after the file, thus Pos = 0 is invalid
		if (bufLen == fileLen && s.CanSeek) Close();
	}

	protected Buffer(Buffer/*!*/ b) { // called in UTF8Buffer constructor
	  Contract.Requires(b != null);
		buf = b.buf;
		bufStart = b.bufStart;
		bufLen = b.bufLen;
		fileLen = b.fileLen;
		bufPos = b.bufPos;
		stream = b.stream;
		// keep destructor from closing the stream
		//b.stream = null;
		isUserStream = b.isUserStream;
		// keep destructor from closing the stream
		b.isUserStream = true;
	}

	~Buffer() { Close(); }

	protected void Close() {
		if (!isUserStream && stream != null) {
			stream.Close();
			//stream = null;
		}
	}

	public virtual int Read () {
		if (bufPos < bufLen) {
			return buf[bufPos++];
		} else if (Pos < fileLen) {
			Pos = Pos; // shift buffer start to Pos
			return buf[bufPos++];
		} else if (stream != null && !stream.CanSeek && ReadNextStreamChunk() > 0) {
			return buf[bufPos++];
		} else {
			return EOF;
		}
	}

	public int Peek () {
		int curPos = Pos;
		int ch = Read();
		Pos = curPos;
		return ch;
	}

	public string/*!*/ GetString (int beg, int end) {
	  Contract.Ensures(Contract.Result<string>() != null);
		int len = 0;
		char[] buf = new char[end - beg];
		int oldPos = Pos;
		Pos = beg;
		while (Pos < end) buf[len++] = (char) Read();
		Pos = oldPos;
		return new String(buf, 0, len);
	}

	public int Pos {
		get { return bufPos + bufStart; }
		set {
			if (value >= fileLen && stream != null && !stream.CanSeek) {
				// Wanted position is after buffer and the stream
				// is not seek-able e.g. network or console,
				// thus we have to read the stream manually till
				// the wanted position is in sight.
				while (value >= fileLen && ReadNextStreamChunk() > 0);
			}

			if (value < 0 || value > fileLen) {
				throw new FatalError("buffer out of bounds access, position: " + value);
			}

			if (value >= bufStart && value < bufStart + bufLen) { // already in buffer
				bufPos = value - bufStart;
			} else if (stream != null) { // must be swapped in
				stream.Seek(value, SeekOrigin.Begin);
				bufLen = stream.Read(buf, 0, buf.Length);
				bufStart = value; bufPos = 0;
			} else {
				// set the position to the end of the file, Pos will return fileLen.
				bufPos = fileLen - bufStart;
			}
		}
	}

	// Read the next chunk of bytes from the stream, increases the buffer
	// if needed and updates the fields fileLen and bufLen.
	// Returns the number of bytes read.
	private int ReadNextStreamChunk() {
		int free = buf.Length - bufLen;
		if (free == 0) {
			// in the case of a growing input stream
			// we can neither seek in the stream, nor can we
			// foresee the maximum length, thus we must adapt
			// the buffer size on demand.
			byte[] newBuf = new byte[bufLen * 2];
			Array.Copy(buf, newBuf, bufLen);
			buf = newBuf;
			free = bufLen;
		}
		int read = stream.Read(buf, bufLen, free);
		if (read > 0) {
			fileLen = bufLen = (bufLen + read);
			return read;
		}
		// end of stream reached
		return 0;
	}
}

//-----------------------------------------------------------------------------------
// UTF8Buffer
//-----------------------------------------------------------------------------------
public class UTF8Buffer: Buffer {
	public UTF8Buffer(Buffer/*!*/ b): base(b) {Contract.Requires(b != null);}

	public override int Read() {
		int ch;
		do {
			ch = base.Read();
			// until we find a utf8 start (0xxxxxxx or 11xxxxxx)
		} while ((ch >= 128) && ((ch & 0xC0) != 0xC0) && (ch != EOF));
		if (ch < 128 || ch == EOF) {
			// nothing to do, first 127 chars are the same in ascii and utf8
			// 0xxxxxxx or end of file character
		} else if ((ch & 0xF0) == 0xF0) {
			// 11110xxx 10xxxxxx 10xxxxxx 10xxxxxx
			int c1 = ch & 0x07; ch = base.Read();
			int c2 = ch & 0x3F; ch = base.Read();
			int c3 = ch & 0x3F; ch = base.Read();
			int c4 = ch & 0x3F;
			ch = (((((c1 << 6) | c2) << 6) | c3) << 6) | c4;
		} else if ((ch & 0xE0) == 0xE0) {
			// 1110xxxx 10xxxxxx 10xxxxxx
			int c1 = ch & 0x0F; ch = base.Read();
			int c2 = ch & 0x3F; ch = base.Read();
			int c3 = ch & 0x3F;
			ch = (((c1 << 6) | c2) << 6) | c3;
		} else if ((ch & 0xC0) == 0xC0) {
			// 110xxxxx 10xxxxxx
			int c1 = ch & 0x1F; ch = base.Read();
			int c2 = ch & 0x3F;
			ch = (c1 << 6) | c2;
		}
		return ch;
	}
}

//-----------------------------------------------------------------------------------
// Scanner
//-----------------------------------------------------------------------------------
public class Scanner {
	const char EOL = '\n';
	const int eofSym = 0; /* pdt */
	const int maxT = 107;
	const int noSym = 107;


	[ContractInvariantMethod]
	void objectInvariant(){
		Contract.Invariant(buffer!=null);
		Contract.Invariant(t != null);
		Contract.Invariant(start != null);
		Contract.Invariant(tokens != null);
		Contract.Invariant(pt != null);
		Contract.Invariant(tval != null);
		Contract.Invariant(Filename != null);
		Contract.Invariant(errorHandler != null);
	}

	public Buffer/*!*/ buffer; // scanner buffer

	Token/*!*/ t;          // current token
	int ch;           // current input character
	int pos;          // byte position of current character
	int charPos;
	int col;          // column number of current character
	int line;         // line number of current character
	int oldEols;      // EOLs that appeared in a comment;
	static readonly Hashtable/*!*/ start; // maps first token character to start state

	Token/*!*/ tokens;     // list of tokens already peeked (first token is a dummy)
	Token/*!*/ pt;         // current peek token

	char[]/*!*/ tval = new char[128]; // text of current token
	int tlen;         // length of current token

	private string/*!*/ Filename;
	private Errors/*!*/ errorHandler;

	static Scanner() {
		start = new Hashtable(128);
		for (int i = 39; i <= 39; ++i) start[i] = 1;
		for (int i = 63; i <= 63; ++i) start[i] = 1;
		for (int i = 65; i <= 90; ++i) start[i] = 1;
		for (int i = 92; i <= 92; ++i) start[i] = 1;
		for (int i = 95; i <= 95; ++i) start[i] = 1;
		for (int i = 98; i <= 122; ++i) start[i] = 1;
		for (int i = 48; i <= 57; ++i) start[i] = 7;
		for (int i = 34; i <= 34; ++i) start[i] = 8;
		start[97] = 12; 
		start[58] = 56; 
		start[123] = 10; 
		start[125] = 11; 
		start[61] = 57; 
		start[124] = 58; 
		start[59] = 19; 
		start[44] = 20; 
		start[40] = 21; 
		start[41] = 22; 
		start[60] = 59; 
		start[62] = 60; 
		start[46] = 61; 
		start[42] = 24; 
		start[96] = 25; 
		start[91] = 28; 
		start[93] = 29; 
		start[8660] = 33; 
		start[8658] = 35; 
		start[38] = 36; 
		start[8743] = 38; 
		start[8744] = 40; 
		start[33] = 62; 
		start[8800] = 44; 
		start[8804] = 45; 
		start[8805] = 46; 
		start[43] = 47; 
		start[45] = 48; 
		start[47] = 49; 
		start[37] = 50; 
		start[172] = 51; 
		start[8704] = 52; 
		start[8707] = 53; 
		start[8226] = 55; 
		start[Buffer.EOF] = -1;

	}

//	[NotDelayed]
	public Scanner (string/*!*/ fileName, Errors/*!*/ errorHandler) : base() {
	  Contract.Requires(fileName != null);
	  Contract.Requires(errorHandler != null);
		this.errorHandler = errorHandler;
		pt = tokens = new Token();  // first token is a dummy
		t = new Token(); // dummy because t is a non-null field
		try {
			Stream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
			buffer = new Buffer(stream, false);
			Filename = fileName;
			Init();
		} catch (IOException) {
			throw new FatalError("Cannot open file " + fileName);
		}
	}

//	[NotDelayed]
	public Scanner (Stream/*!*/ s, Errors/*!*/ errorHandler, string/*!*/ fileName) : base() {
	  Contract.Requires(s != null);
	  Contract.Requires(errorHandler != null);
	  Contract.Requires(fileName != null);
		pt = tokens = new Token();  // first token is a dummy
		t = new Token(); // dummy because t is a non-null field
		buffer = new Buffer(s, true);
		this.errorHandler = errorHandler;
		this.Filename = fileName;
		Init();
	}

	void Init() {
		pos = -1; line = 1; col = 0;
		oldEols = 0;
		NextCh();
		if (ch == 0xEF) { // check optional byte order mark for UTF-8
			NextCh(); int ch1 = ch;
			NextCh(); int ch2 = ch;
			if (ch1 != 0xBB || ch2 != 0xBF) {
				throw new FatalError(String.Format("illegal byte order mark: EF {0,2:X} {1,2:X}", ch1, ch2));
			}
			buffer = new UTF8Buffer(buffer); col = 0;
			NextCh();
		}
		pt = tokens = new Token();  // first token is a dummy
	}

	string/*!*/ ReadToEOL(){
	Contract.Ensures(Contract.Result<string>() != null);
	  int p = buffer.Pos;
	  int ch = buffer.Read();
	  // replace isolated '\r' by '\n' in order to make
	  // eol handling uniform across Windows, Unix and Mac
	  if (ch == '\r' && buffer.Peek() != '\n') ch = EOL;
	  while (ch != EOL && ch != Buffer.EOF){
		ch = buffer.Read();
		// replace isolated '\r' by '\n' in order to make
		// eol handling uniform across Windows, Unix and Mac
		if (ch == '\r' && buffer.Peek() != '\n') ch = EOL;
	  }
	  string/*!*/ s = buffer.GetString(p, buffer.Pos);
	  Contract.Assert(s!=null);
	  return s;
	}

	void NextCh() {
		if (oldEols > 0) { ch = EOL; oldEols--; }
		else {
//			pos = buffer.Pos;
//			ch = buffer.Read(); col++;
//			// replace isolated '\r' by '\n' in order to make
//			// eol handling uniform across Windows, Unix and Mac
//			if (ch == '\r' && buffer.Peek() != '\n') ch = EOL;
//			if (ch == EOL) { line++; col = 0; }

			while (true) {
				pos = buffer.Pos;
				ch = buffer.Read(); col++;
				// replace isolated '\r' by '\n' in order to make
				// eol handling uniform across Windows, Unix and Mac
				if (ch == '\r' && buffer.Peek() != '\n') ch = EOL;
				if (ch == EOL) {
					line++; col = 0;
				} else if (ch == '#' && col == 1) {
				  int prLine = line;
				  int prColumn = 0;

				  string/*!*/ hashLine = ReadToEOL();
				  Contract.Assert(hashLine!=null);
				  col = 0;
				  line++;

				  hashLine = hashLine.TrimEnd(null);
				  if (hashLine.StartsWith("line ") || hashLine == "line") {
					// parse #line pragma:  #line num [filename]
					string h = hashLine.Substring(4).TrimStart(null);
					int x = h.IndexOf(' ');
					if (x == -1) {
					  x = h.Length;  // this will be convenient below when we look for a filename
					}
					try {
					  int li = int.Parse(h.Substring(0, x));

					  h = h.Substring(x).Trim();

					  // act on #line
					  line = li;
					  if (h.Length != 0) {
						// a filename was specified
						Filename = h;
					  }
					  continue;  // successfully parsed and acted on the #line pragma

					} catch (FormatException) {
					  // just fall down through to produce an error message
					}
					this.errorHandler.SemErr(Filename, prLine, prColumn, "Malformed (#line num [filename]) pragma: #" + hashLine);
					continue;
				  }

				  this.errorHandler.SemErr(Filename, prLine, prColumn, "Unrecognized pragma: #" + hashLine);
				  continue;
				}
				return;
			  }


		}

	}

	void AddCh() {
		if (tlen >= tval.Length) {
			char[] newBuf = new char[2 * tval.Length];
			Array.Copy(tval, 0, newBuf, 0, tval.Length);
			tval = newBuf;
		}
		if (ch != Buffer.EOF) {
			tval[tlen++] = (char) ch;
			NextCh();
		}
	}



	bool Comment0() {
		int level = 1, pos0 = pos, line0 = line, col0 = col, charPos0 = charPos;
		NextCh();
		if (ch == '/') {
			NextCh();
			for(;;) {
				if (ch == 10) {
					level--;
					if (level == 0) { oldEols = line - line0; NextCh(); return true; }
					NextCh();
				} else if (ch == Buffer.EOF) return false;
				else NextCh();
			}
		} else {
			buffer.Pos = pos0; NextCh(); line = line0; col = col0; charPos = charPos0;
		}
		return false;
	}

	bool Comment1() {
		int level = 1, pos0 = pos, line0 = line, col0 = col, charPos0 = charPos;
		NextCh();
		if (ch == '*') {
			NextCh();
			for(;;) {
				if (ch == '*') {
					NextCh();
					if (ch == '/') {
						level--;
						if (level == 0) { oldEols = line - line0; NextCh(); return true; }
						NextCh();
					}
				} else if (ch == '/') {
					NextCh();
					if (ch == '*') {
						level++; NextCh();
					}
				} else if (ch == Buffer.EOF) return false;
				else NextCh();
			}
		} else {
			buffer.Pos = pos0; NextCh(); line = line0; col = col0; charPos = charPos0;
		}
		return false;
	}


	void CheckLiteral() {
		switch (t.val) {
			case "ghost": t.kind = 8; break;
			case "module": t.kind = 9; break;
			case "refines": t.kind = 10; break;
			case "imports": t.kind = 11; break;
			case "class": t.kind = 12; break;
			case "static": t.kind = 13; break;
			case "datatype": t.kind = 14; break;
			case "codatatype": t.kind = 15; break;
			case "var": t.kind = 19; break;
			case "type": t.kind = 21; break;
			case "method": t.kind = 27; break;
			case "constructor": t.kind = 28; break;
			case "returns": t.kind = 29; break;
			case "modifies": t.kind = 31; break;
			case "free": t.kind = 32; break;
			case "requires": t.kind = 33; break;
			case "ensures": t.kind = 34; break;
			case "decreases": t.kind = 35; break;
			case "bool": t.kind = 36; break;
			case "nat": t.kind = 37; break;
			case "int": t.kind = 38; break;
			case "set": t.kind = 39; break;
			case "multiset": t.kind = 40; break;
			case "seq": t.kind = 41; break;
			case "map": t.kind = 42; break;
			case "object": t.kind = 43; break;
			case "function": t.kind = 45; break;
			case "predicate": t.kind = 46; break;
			case "reads": t.kind = 47; break;
			case "label": t.kind = 50; break;
			case "break": t.kind = 51; break;
			case "return": t.kind = 52; break;
			case "assume": t.kind = 55; break;
			case "new": t.kind = 56; break;
			case "choose": t.kind = 59; break;
			case "if": t.kind = 60; break;
			case "else": t.kind = 61; break;
			case "case": t.kind = 62; break;
			case "while": t.kind = 64; break;
			case "invariant": t.kind = 65; break;
			case "match": t.kind = 66; break;
			case "assert": t.kind = 67; break;
			case "print": t.kind = 68; break;
			case "parallel": t.kind = 69; break;
			case "in": t.kind = 82; break;
			case "false": t.kind = 92; break;
			case "true": t.kind = 93; break;
			case "null": t.kind = 94; break;
			case "this": t.kind = 95; break;
			case "fresh": t.kind = 96; break;
			case "allocated": t.kind = 97; break;
			case "old": t.kind = 98; break;
			case "then": t.kind = 99; break;
			case "forall": t.kind = 101; break;
			case "exists": t.kind = 103; break;
			default: break;
		}
	}

	Token/*!*/ NextToken() {
	  Contract.Ensures(Contract.Result<Token>() != null);
		while (ch == ' ' ||
			ch >= 9 && ch <= 10 || ch == 13
		) NextCh();
		if (ch == '/' && Comment0() ||ch == '/' && Comment1()) return NextToken();
		int recKind = noSym;
		int recEnd = pos;
		t = new Token();
		t.pos = pos; t.col = col; t.line = line;
		t.filename = this.Filename;
		int state;
		if (start.ContainsKey(ch)) {
			Contract.Assert(start[ch] != null);
			state = (int) start[ch];
		}
		else { state = 0; }
		tlen = 0; AddCh();

		switch (state) {
			case -1: { t.kind = eofSym; break; } // NextCh already done
			case 0: {
				if (recKind != noSym) {
					tlen = recEnd - t.pos;
					SetScannerBehindT();
				}
				t.kind = recKind; break;
			} // NextCh already done
			case 1:
				recEnd = pos; recKind = 1;
				if (ch == 39 || ch >= '0' && ch <= '9' || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 1;}
				else {t.kind = 1; t.val = new String(tval, 0, tlen); CheckLiteral(); return t;}
			case 2:
				recEnd = pos; recKind = 1;
				if (ch == 39 || ch >= '0' && ch <= '9' || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 2;}
				else {t.kind = 1; t.val = new String(tval, 0, tlen); CheckLiteral(); return t;}
			case 3:
				recEnd = pos; recKind = 1;
				if (ch == 39 || ch >= '0' && ch <= '9' || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 3;}
				else {t.kind = 1; t.val = new String(tval, 0, tlen); CheckLiteral(); return t;}
			case 4:
				recEnd = pos; recKind = 1;
				if (ch == 39 || ch >= '0' && ch <= '9' || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 4;}
				else {t.kind = 1; t.val = new String(tval, 0, tlen); CheckLiteral(); return t;}
			case 5:
				recEnd = pos; recKind = 1;
				if (ch == 39 || ch >= '0' && ch <= '9' || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 5;}
				else {t.kind = 1; t.val = new String(tval, 0, tlen); CheckLiteral(); return t;}
			case 6:
				recEnd = pos; recKind = 1;
				if (ch == 39 || ch >= '0' && ch <= '9' || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 6;}
				else {t.kind = 1; t.val = new String(tval, 0, tlen); CheckLiteral(); return t;}
			case 7:
				recEnd = pos; recKind = 2;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 7;}
				else {t.kind = 2; break;}
			case 8:
				if (ch == '"') {AddCh(); goto case 9;}
				else if (ch >= ' ' && ch <= '!' || ch >= '#' && ch <= '~') {AddCh(); goto case 8;}
				else {goto case 0;}
			case 9:
				{t.kind = 4; break;}
			case 10:
				{t.kind = 6; break;}
			case 11:
				{t.kind = 7; break;}
			case 12:
				recEnd = pos; recKind = 1;
				if (ch == 39 || ch >= '0' && ch <= '9' || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'a' && ch <= 'q' || ch >= 's' && ch <= 'z') {AddCh(); goto case 2;}
				else if (ch == 'r') {AddCh(); goto case 14;}
				else {t.kind = 1; t.val = new String(tval, 0, tlen); CheckLiteral(); return t;}
			case 13:
				recEnd = pos; recKind = 1;
				if (ch == 39 || ch >= '0' && ch <= '9' || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 13;}
				else {t.kind = 1; t.val = new String(tval, 0, tlen); CheckLiteral(); return t;}
			case 14:
				recEnd = pos; recKind = 1;
				if (ch == 39 || ch >= '0' && ch <= '9' || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'a' && ch <= 'q' || ch >= 's' && ch <= 'z') {AddCh(); goto case 3;}
				else if (ch == 'r') {AddCh(); goto case 15;}
				else {t.kind = 1; t.val = new String(tval, 0, tlen); CheckLiteral(); return t;}
			case 15:
				recEnd = pos; recKind = 1;
				if (ch == 39 || ch >= '0' && ch <= '9' || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'b' && ch <= 'z') {AddCh(); goto case 4;}
				else if (ch == 'a') {AddCh(); goto case 16;}
				else {t.kind = 1; t.val = new String(tval, 0, tlen); CheckLiteral(); return t;}
			case 16:
				recEnd = pos; recKind = 1;
				if (ch == 39 || ch >= '0' && ch <= '9' || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'a' && ch <= 'x' || ch == 'z') {AddCh(); goto case 5;}
				else if (ch == 'y') {AddCh(); goto case 17;}
				else {t.kind = 1; t.val = new String(tval, 0, tlen); CheckLiteral(); return t;}
			case 17:
				recEnd = pos; recKind = 3;
				if (ch == 39 || ch == '0' || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 6;}
				else if (ch >= '1' && ch <= '9') {AddCh(); goto case 18;}
				else {t.kind = 3; break;}
			case 18:
				recEnd = pos; recKind = 3;
				if (ch == 39 || ch == '?' || ch >= 'A' && ch <= 'Z' || ch == 92 || ch == '_' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 13;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 18;}
				else {t.kind = 3; break;}
			case 19:
				{t.kind = 18; break;}
			case 20:
				{t.kind = 20; break;}
			case 21:
				{t.kind = 22; break;}
			case 22:
				{t.kind = 24; break;}
			case 23:
				{t.kind = 30; break;}
			case 24:
				{t.kind = 48; break;}
			case 25:
				{t.kind = 49; break;}
			case 26:
				{t.kind = 53; break;}
			case 27:
				{t.kind = 54; break;}
			case 28:
				{t.kind = 57; break;}
			case 29:
				{t.kind = 58; break;}
			case 30:
				{t.kind = 63; break;}
			case 31:
				if (ch == '>') {AddCh(); goto case 32;}
				else {goto case 0;}
			case 32:
				{t.kind = 70; break;}
			case 33:
				{t.kind = 71; break;}
			case 34:
				{t.kind = 72; break;}
			case 35:
				{t.kind = 73; break;}
			case 36:
				if (ch == '&') {AddCh(); goto case 37;}
				else {goto case 0;}
			case 37:
				{t.kind = 74; break;}
			case 38:
				{t.kind = 75; break;}
			case 39:
				{t.kind = 76; break;}
			case 40:
				{t.kind = 77; break;}
			case 41:
				{t.kind = 79; break;}
			case 42:
				{t.kind = 80; break;}
			case 43:
				{t.kind = 81; break;}
			case 44:
				{t.kind = 84; break;}
			case 45:
				{t.kind = 85; break;}
			case 46:
				{t.kind = 86; break;}
			case 47:
				{t.kind = 87; break;}
			case 48:
				{t.kind = 88; break;}
			case 49:
				{t.kind = 89; break;}
			case 50:
				{t.kind = 90; break;}
			case 51:
				{t.kind = 91; break;}
			case 52:
				{t.kind = 102; break;}
			case 53:
				{t.kind = 104; break;}
			case 54:
				{t.kind = 105; break;}
			case 55:
				{t.kind = 106; break;}
			case 56:
				recEnd = pos; recKind = 5;
				if (ch == '=') {AddCh(); goto case 26;}
				else if (ch == '|') {AddCh(); goto case 27;}
				else if (ch == ':') {AddCh(); goto case 54;}
				else {t.kind = 5; break;}
			case 57:
				recEnd = pos; recKind = 16;
				if (ch == '=') {AddCh(); goto case 63;}
				else if (ch == '>') {AddCh(); goto case 30;}
				else {t.kind = 16; break;}
			case 58:
				recEnd = pos; recKind = 17;
				if (ch == '|') {AddCh(); goto case 39;}
				else {t.kind = 17; break;}
			case 59:
				recEnd = pos; recKind = 25;
				if (ch == '=') {AddCh(); goto case 64;}
				else {t.kind = 25; break;}
			case 60:
				recEnd = pos; recKind = 26;
				if (ch == '=') {AddCh(); goto case 41;}
				else {t.kind = 26; break;}
			case 61:
				recEnd = pos; recKind = 44;
				if (ch == '.') {AddCh(); goto case 65;}
				else {t.kind = 44; break;}
			case 62:
				recEnd = pos; recKind = 83;
				if (ch == '=') {AddCh(); goto case 42;}
				else if (ch == '!') {AddCh(); goto case 43;}
				else {t.kind = 83; break;}
			case 63:
				recEnd = pos; recKind = 23;
				if (ch == '>') {AddCh(); goto case 34;}
				else {t.kind = 23; break;}
			case 64:
				recEnd = pos; recKind = 78;
				if (ch == '=') {AddCh(); goto case 31;}
				else {t.kind = 78; break;}
			case 65:
				recEnd = pos; recKind = 100;
				if (ch == '.') {AddCh(); goto case 23;}
				else {t.kind = 100; break;}

		}
		t.val = new String(tval, 0, tlen);
		return t;
	}

	private void SetScannerBehindT() {
		buffer.Pos = t.pos;
		NextCh();
		line = t.line; col = t.col;
		for (int i = 0; i < tlen; i++) NextCh();
	}

	// get the next token (possibly a token already seen during peeking)
	public Token/*!*/ Scan () {
	 Contract.Ensures(Contract.Result<Token>() != null);
		if (tokens.next == null) {
			return NextToken();
		} else {
			pt = tokens = tokens.next;
			return tokens;
		}
	}

	// peek for the next token, ignore pragmas
	public Token/*!*/ Peek () {
	  Contract.Ensures(Contract.Result<Token>() != null);
		do {
			if (pt.next == null) {
				pt.next = NextToken();
			}
			pt = pt.next;
		} while (pt.kind > maxT); // skip pragmas

		return pt;
	}

	// make sure that peeking starts at the current scan position
	public void ResetPeek () { pt = tokens; }

} // end Scanner

public delegate void ErrorProc(int n, string filename, int line, int col);


}