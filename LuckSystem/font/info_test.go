package font

import (
	"bytes"
	"flag"
	"fmt"
	"os"
	"testing"

	"github.com/go-restruct/restruct"
)

func TestSetCharMappingKeepsMovedCharacter(t *testing.T) {
	restruct.EnableExprBeta()
	info := &Info{
		CharNum:      4,
		DrawSize:     make([]DrawSize, 4),
		UnicodeIndex: make([]uint16, 65536),
		UnicodeSize:  make([]CharSize, 65536),
		IndexUnicode: []rune{0, 'b', 'c', 'd'},
	}
	info.UnicodeIndex['b'] = 1
	info.UnicodeIndex['c'] = 2
	info.UnicodeIndex['d'] = 3

	// Replace b,c,d with x,d,c. The old implementation first assigned d to
	// index 2 and then cleared that new mapping while processing old index 3.
	info.setCharMapping(1, 'x')
	info.setCharMapping(2, 'd')
	info.setCharMapping(3, 'c')

	if got := info.UnicodeIndex['d']; got != 2 {
		t.Fatalf("moved character mapping was lost: got index %d, want 2", got)
	}
	if got := info.UnicodeIndex['c']; got != 3 {
		t.Fatalf("replacement character has index %d, want 3", got)
	}
	if got := info.UnicodeIndex['x']; got != 1 {
		t.Fatalf("new character has index %d, want 1", got)
	}
	if got := info.UnicodeIndex['b']; got != 0 {
		t.Fatalf("replaced character still has index %d, want 0", got)
	}

	var encoded bytes.Buffer
	if err := info.Write(&encoded); err != nil {
		t.Fatal(err)
	}
	reloaded := LoadFontInfo(encoded.Bytes())
	if got := reloaded.UnicodeIndex['d']; got != 2 {
		t.Fatalf("moved character mapping was lost after reload: got index %d, want 2", got)
	}
	if got := reloaded.IndexUnicode[2]; got != 'd' {
		t.Fatalf("reloaded index 2 contains %q, want %q", got, 'd')
	}
}

func TestExtendedCountPreservedAfterEdit(t *testing.T) {
	restruct.EnableExprBeta()
	info := &Info{
		FontSize:      16,
		BlockSize:     17,
		CharNum:       101,
		ExtendedCount: true,
		DrawSize:      make([]DrawSize, 101),
		UnicodeIndex:  make([]uint16, 65536),
		UnicodeSize:   make([]CharSize, 65536),
	}
	info.UnicodeIndex['A'] = 100
	var original bytes.Buffer
	if err := info.Write(&original); err != nil {
		t.Fatal(err)
	}
	loaded := LoadFontInfo(original.Bytes())
	loaded.CharNum++
	loaded.DrawSize = append(loaded.DrawSize, DrawSize{})
	var edited bytes.Buffer
	if err := loaded.Write(&edited); err != nil {
		t.Fatal(err)
	}
	if got := edited.Bytes()[4:8]; !bytes.Equal(got, []byte{100, 0, 102, 0}) {
		t.Fatalf("extended count header changed: %v", got)
	}
	if got, want := edited.Len(), original.Len()+3; got != want {
		t.Fatalf("edited info length = %d, want %d", got, want)
	}
	if got := LoadFontInfo(edited.Bytes()).UnicodeIndex['A']; got != 100 {
		t.Fatalf("character mapping shifted: got %d, want 100", got)
	}
}

func TestMain(m *testing.M) {
	flag.Set("alsologtostderr", "true")
	flag.Set("log_dir", "log")
	flag.Set("v", "10")
	flag.Parse()

	ret := m.Run()
	os.Exit(ret)
}
func TestInfo(t *testing.T) {
	restruct.EnableExprBeta()
	list := []string{"info32"}
	for _, name := range list {
		file := "../data/LB_EN/IMAGE/" + name
		data, _ := os.ReadFile(file)
		info := LoadFontInfo(data)
		txtFile, _ := os.Create(file + ".txt")
		info.Export(txtFile)
		fmt.Println(info.CharNum, " ", len(info.IndexUnicode))
		infoFile, _ := os.Create(file + ".out")
		info.Write(infoFile)

		infoFile.Close()
		txtFile.Close()
	}
}
func TestInfo2(t *testing.T) {
	restruct.EnableExprBeta()
	file := "../data/Other/Font/info32e.info"
	data, _ := os.ReadFile(file)
	info := LoadFontInfo(data)
	txtFile, _ := os.Create(file + ".txt")
	info.Export(txtFile)
	fmt.Println(info.CharNum, " ", len(info.IndexUnicode))
	infoFile, _ := os.Create(file + ".out")
	info.Write(infoFile)

}
func TestStr(t *testing.T) {
	aaa := []int{1, 2, 3}
	index := 3
	bbb := make([]int, 10)
	copy(bbb, aaa[:index])

	fmt.Println(bbb)
}
func TestInfo_Export(t *testing.T) {
	restruct.EnableExprBeta()
	var err error
	savePath := "../data/LB_EN/FONT/"
	infoFiles := []string{"info32", "info24"}

	//============
	for _, name := range infoFiles {
		data, _ := os.ReadFile(savePath + name)
		info := LoadFontInfo(data)
		fmt.Println(name, info.CharNum, len(info.IndexUnicode))
		fs, _ := os.Create(savePath + name + "_export.txt")
		err = info.Export(fs)
		if err != nil {
			panic(err)
		}
		err = fs.Close()
		if err != nil {
			panic(err)
		}
	}

}

func TestInfo_Import(t *testing.T) {
	restruct.EnableExprBeta()
	var err error
	loadPath := "../data/LB_EN/FONT/"
	infoFiles := []string{"info32", "info24"}
	addChars := "!@#$%.,abcdefgAB CDEFG12345测试中文汉字"
	font, _ := os.Open("../data/Other/Font/ARHei-400.ttf")
	defer font.Close()
	//============
	fmt.Println()
	for _, name := range infoFiles {
		data, _ := os.ReadFile(loadPath + name)
		info := LoadFontInfo(data)
		fmt.Println(name, info.CharNum, len(info.IndexUnicode))

		err = info.Import(font, 0, true, "")
		if err != nil {
			panic(err)
		}
		fmt.Println(name, info.CharNum, len(info.IndexUnicode))
		fs, _ := os.Create(loadPath + name + "_byOnlyRedraw")
		err = info.Write(fs)
		if err != nil {
			panic(err)
		}
		err = fs.Close()
		if err != nil {
			panic(err)
		}
	}

	//============
	fmt.Println()
	for _, name := range infoFiles {
		data, _ := os.ReadFile(loadPath + name)
		info := LoadFontInfo(data)
		fmt.Println(name, info.CharNum, len(info.IndexUnicode))

		err = info.Import(font, int(info.CharNum), false, addChars)
		if err != nil {
			panic(err)
		}
		fmt.Println(name, info.CharNum, len(info.IndexUnicode))
		fs, _ := os.Create(loadPath + name + "_byAddChar")
		err = info.Write(fs)
		if err != nil {
			panic(err)
		}
		err = fs.Close()
		if err != nil {
			panic(err)
		}
	}

	//============
	fmt.Println()
	for _, name := range infoFiles {
		data, _ := os.ReadFile(loadPath + name)
		info := LoadFontInfo(data)
		fmt.Println(name, info.CharNum, len(info.IndexUnicode))

		err = info.Import(font, int(info.CharNum), true, addChars)
		if err != nil {
			panic(err)
		}
		fmt.Println(name, info.CharNum, len(info.IndexUnicode))
		fs, _ := os.Create(loadPath + name + "_byAddCharRedraw")
		err = info.Write(fs)
		if err != nil {
			panic(err)
		}
		err = fs.Close()
		if err != nil {
			panic(err)
		}
	}
}
