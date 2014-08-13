// RUN: %dafny /compile:0 /print:"%t.print" /dprint:"%t.dprint" "%s" > "%t"
// RUN: %diff "%s.expect" "%t"


trait J 
{
	function method F(k:int, y: array<int>): int
	  reads y;
	  decreases k;
	
	function method G(y: int): int 
	{ 
	   100 
	}
	
	method M(y: int) returns (kobra:int)
	  requires y > 0;
	  ensures kobra > 0;
	   
	method N(y: int) 
	{ 
	   var x: int;
	   var y : int;
	   y := 10;
	   x := 1000;
	   y := y + x;
	}
	
	method arrM (y: array<int>, x: int, a: int, b: int) returns (c: int)
	  requires a > b;
	  requires y != null && y.Length > 0;
	  ensures c == a + b;
	  modifies y;
	  decreases x;
}

class C extends J 
{
	function method F(kk:int, yy: array<int>): int
	{ 
	  200 
	}

	method M(kk:int) returns (ksos:int) //errors here, M must provide its own specifications
	{
		ksos:=10;	
	}

	method arrM (y1: array<int>, x1: int, a1: int, b1: int) returns (c1: int)
	  requires a1 > b1;
	  requires y1 != null && y1.Length > 0;
	  ensures c1 == a1 + b1;
	  modifies y1;
	  decreases x1;	
	{
	    y1[0] := a1 + b1;
            c1 := a1 + b1;
        }	
}