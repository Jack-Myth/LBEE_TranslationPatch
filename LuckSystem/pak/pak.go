package pak

import (
	"bufio"
	"bytes"
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"os"
	"path"
	"path/filepath"
	"strconv"
	"strings"

	"lucksystem/charset"
	"lucksystem/utils"

	"github.com/go-restruct/restruct"
	"github.com/golang/glog"
)

type Header struct {
	HeaderLength uint32
	FileCount    uint32
	IDStart      uint32 // 图像包中，id段开始
	BlockSize    uint32

	Unk2 uint32
	Unk3 uint32
	Unk4 uint32
	Unk5 uint32

	Flags uint32
	// 4 * 9 = 36
}

type Entry struct {
	Offset  uint32
	Length  uint32
	Data    []byte `struct:"-"`
	Name    string `struct:"-"`
	ID      int    `struct:"-"`
	Replace bool   `struct:"-"`
}

type Pak struct {
	Header    `struct:"-"`
	Files     []*Entry        `struct:"size=FileCount"`
	NameMap   map[string]int  `struct:"-"`
	FileName  string          `struct:"-"`
	Coding    charset.Charset `struct:"-"`
	OffsetPos int64           `struct:"-"` // Files 数据开始位置
	DataPos   int64           `struct:"-"` // Files.Data 数据开始位置
	Rebuild   bool            `struct:"-"` // 替换数据后，是否需要重构pak
}

func LoadPak(filename string, coding charset.Charset) *Pak {

	pakFile := &Pak{}
	pakFile.Load(filename, coding)
	return pakFile
}
func (p *Pak) Load(filename string, coding charset.Charset) {

	p.Rebuild = false
	p.FileName = filename
	p.Coding = coding
	if len(coding) == 0 {
		p.Coding = charset.UTF_8
	}
	err := p.open()
	if err != nil {
		glog.Fatalln(err)
	}
}
func (p *Pak) open() error {
	f, err := os.Open(p.FileName)

	if err != nil {
		glog.V(8).Infoln("os.Open", err)
		return err
	}
	defer f.Close()

	headerLenBytes := make([]byte, 4)
	_, err = f.ReadAt(headerLenBytes, 0)
	if err != nil {
		return err
	}
	headerLen := binary.LittleEndian.Uint32(headerLenBytes)

	data := make([]byte, headerLen)
	_, err = f.ReadAt(data, 0)
	if err != nil {
		return err
	}
	err = restruct.Unpack(data, binary.LittleEndian, &p.Header)
	if err != nil {
		glog.V(8).Infoln("restruct.Unpack1", err)
		return err
	}

	temp := make([]byte, 4)
	tempPos := int64(32)
	_, err = f.ReadAt(temp, tempPos)
	if err != nil {
		return err
	}
	for binary.LittleEndian.Uint32(temp) != p.HeaderLength/p.BlockSize {
		tempPos += 4
		_, err = f.ReadAt(temp, tempPos)
		if err != nil {
			return err
		}
	}

	// 文件偏移 长度读取
	p.OffsetPos = tempPos
	offData := data[tempPos : tempPos+int64(8*p.FileCount)]
	err = restruct.Unpack(offData, binary.LittleEndian, p)
	if err != nil {
		glog.V(8).Infoln("restruct.Unpack2", err)
		return err
	}
	// 读取文件名
	named := (p.Flags & 512) != 0
	if named {

		_, err = f.ReadAt(temp, tempPos-4)
		if err != nil {
			return err
		}
		offset := int(binary.LittleEndian.Uint32(temp))
		size := 0
		for _, file := range p.Files {
			for data[offset+size] != 0x00 {
				size++
			}
			file.Name, err = charset.ToUTF8(p.Coding, data[offset:offset+size])

			if err != nil {
				return err
			}
			//fmt.Println(file.Name, file.Offset, file.Length)
			offset += size + 1
			size = 0

		}
	}

	p.NameMap = make(map[string]int)
	// 读取文件数据
	for i, file := range p.Files {
		if !named {
			file.Name = strconv.Itoa(i)
		}

		file.ID = int(p.IDStart) + i

		p.NameMap[file.Name] = file.ID
		file.Offset *= p.BlockSize
		file.Replace = false
		// file.Data = data[file.Offset : file.Offset+file.Length]

		if i == 0 {
			p.DataPos = int64(file.Offset) // 第一个文件数据的位置
		}
	}

	return nil
}
func (p *Pak) ReadAll() []*Entry {
	f, err := os.Open(p.FileName)
	if err != nil {
		glog.V(8).Infoln("os.Open", err)
		return nil
	}
	defer f.Close()
	for _, e := range p.Files {
		e.Data = make([]byte, e.Length)
		f.ReadAt(e.Data, int64(e.Offset))
	}
	return p.Files
}

func (p *Pak) Get(name string) (*Entry, error) {

	id, has := p.NameMap[name]
	if !has {
		return nil, errors.New("文件不存在")
	}
	return p.GetById(id)
}

func (p *Pak) GetById(id int) (*Entry, error) {
	return p.GetByIndex(id - int(p.IDStart))
}

func (p *Pak) GetByIndex(index int) (*Entry, error) {

	if index < 0 || index >= int(p.FileCount) {
		return nil, errors.New("文件id错误")
	}

	entry := p.Files[index]
	if entry.Offset == 0 && entry.Data != nil && len(entry.Data) > 0 {
		// 外部数据
		return entry, nil
	}
	f, err := os.Open(p.FileName)
	if err != nil {
		return nil, err
	}
	defer f.Close()

	entry.Data = make([]byte, entry.Length)
	_, err = f.ReadAt(entry.Data, int64(entry.Offset))
	if err != nil {
		return nil, err
	}
	return entry, nil
}
func (p *Pak) CheckName(name string) bool {
	_, has := p.NameMap[name]
	return has
}

// Set 设置外部文件替换pak文件
//
//	Description
//	Receiver p *Pak
//	Param name string
//	Param r io.Reader
//	Return error
func (p *Pak) Set(name string, r io.Reader) error {
	_, has := p.NameMap[name]
	if !has {
		return errors.New("文件不存在")
	}
	data, err := io.ReadAll(r)
	if err != nil {
		return err
	}
	for index, entry := range p.Files {
		if entry.Name == name {
			if err := p.SetByIndex(index, bytes.NewReader(data)); err != nil {
				return err
			}
		}
	}
	return nil
}

func (p *Pak) CheckId(id int) bool {
	return p.CheckIndex(id - int(p.IDStart))
}
func (p *Pak) SetById(id int, r io.Reader) error {
	return p.SetByIndex(id-int(p.IDStart), r)
}
func (p *Pak) CheckIndex(index int) bool {
	return !(index < 0 || index >= int(p.FileCount))
}
func (p *Pak) SetByIndex(index int, r io.Reader) error {
	if index < 0 || index >= int(p.FileCount) {
		return errors.New("文件id错误")
	}
	entry := p.Files[index]
	newData, err := io.ReadAll(r)
	if err != nil {
		glog.Warning("os.ReadFile", err)
		return err
	}
	alignLength := entry.Length
	if alignLength/p.BlockSize*p.BlockSize != alignLength {
		alignLength = (alignLength/p.BlockSize + 1) * p.BlockSize
	}

	p.Rebuild = p.Rebuild || alignLength < uint32(len(newData)) // 无法装下新数据内容，需要重构

	if uint32(len(newData)) != entry.Length {
		glog.V(4).Infof("%s\nlength: %d -> length: %d\n",
			entry.Name, entry.Length, uint32(len(newData)))
	}
	entry.Replace = true
	entry.Data = newData
	entry.Length = uint32(len(newData))
	return nil
}

// Write
//
//	Description
//	Receiver p *Pak
//	Param w io.Writer 必须实现 io.WriterAt
//	Return error
func (p *Pak) Write(w io.Writer) error {

	oldOffset := make(map[int]uint32, p.FileCount)
	if p.Rebuild {
		offset := uint32(0)
		flag := false
		for i, f := range p.Files {
			if flag {
				if offset/p.BlockSize*p.BlockSize != offset {
					offset = (offset/p.BlockSize + 1) * p.BlockSize
				}
				oldOffset[i] = f.Offset //保存旧的offset
				f.Offset = offset       // 更新Offset
				offset += f.Length
			}

			if f.Replace { // 资源发生替换，从下一个开始，重新计算offset
				flag = true
				offset = f.Offset + f.Length
			}
		}
	}
	oldFile, err := os.Open(p.FileName)
	if err != nil {
		glog.V(8).Infoln("os.Open.oldFile", err)
		return err
	}
	defer oldFile.Close()

	file, ok := w.(io.WriterAt)
	if !ok {
		glog.V(8).Infoln("w.(io.WriterAt)", err)
		return err
	}
	// 1. 复制文件全部内容
	_, err = io.Copy(w, oldFile)
	if err != nil {
		glog.V(8).Infoln("io.Copy", err)
		return err
	}
	// 2. 写入偏移和长度和数据
	temp := make([]byte, 4)
	flag := false
	if p.Rebuild {
		for i, f := range p.Files {
			if f.Replace { // 资源发生替换，从这里开始写入数据
				flag = true
			}
			if flag {
				binary.LittleEndian.PutUint32(temp, f.Offset/p.BlockSize)
				file.WriteAt(temp, p.OffsetPos+int64(i*8))
				binary.LittleEndian.PutUint32(temp, f.Length)
				file.WriteAt(temp, p.OffsetPos+int64(i*8+4))
				if f.Replace {
					file.WriteAt(f.Data, int64(f.Offset))
				} else {
					data := make([]byte, f.Length)
					oldFile.ReadAt(data, int64(oldOffset[i]))
					file.WriteAt(data, int64(f.Offset))
				}
			}
		}
	} else {
		for i, f := range p.Files {
			if f.Replace {
				binary.LittleEndian.PutUint32(temp, f.Length)
				file.WriteAt(temp, p.OffsetPos+int64(i*8+4))
				file.WriteAt(f.Data, int64(f.Offset))
			}
		}
	}

	return nil
}

// Export
//
//	Description
//	Receiver p *Pak
//	Param w io.Writer
//	Param mode string 可选 all,index,id,name
//	Param value interface{}
//	    mode=="all": w为每行一个文件路径的txt文件
//	      value 	dir string 	保存文件夹路径
//	    mode=="index":
//	      value 	index 	int 	从0开始的文件序号
//	    mode=="id":
//	      value	id	int	文件唯一ID
//	    mode=="name":
//	      value	name	string	文件名
//	Return error
func (p *Pak) Export(w io.Writer, mode string, value interface{}) error {
	var err error
	switch mode {
	case "all":
		dir, _ := filepath.Abs(value.(string))
		if _, err = os.Stat(dir); os.IsNotExist(err) {
			os.Mkdir(dir, os.ModePerm)
		}
		fes := p.ReadAll()
		for _, e := range fes {
			file := ""
			line := ""
			if len(e.Name) != 0 {

				file = path.Join(dir, e.Name)
				line = fmt.Sprintf("name:%s,%s\n", e.Name, file)
			} else {
				file = path.Join(dir, strconv.Itoa(e.ID))
				line = fmt.Sprintf("id:%d,%s\n", e.ID, file)
			}
			_, err = w.Write([]byte(line))
			if err != nil {
				return err
			}
			err = os.WriteFile(file, e.Data, 0666)
			if err != nil {
				return err
			}
		}
	case "index":
		fallthrough
	case "id":
		fallthrough
	case "name":
		var e *Entry
		switch mode {
		case "index":
			e, err = p.GetByIndex(value.(int))
		case "id":
			e, err = p.GetById(value.(int))
		case "name":
			e, err = p.Get(value.(string))
		}
		if err != nil {
			return err
		}
		_, err = w.Write(e.Data)
		if err != nil {
			return err
		}
	default:
		return errors.New("pak.mode error")
	}
	return nil
}

// Import
//
//	Description
//	Receiver p *Pak
//	Param r io.Reader 导入文件
//	Param mode string 可选file,list,dir
//	Param value interface{}
//	    mode=="file": r为导入文件，根据类型自动判断是文件名还是id，不支持index
//	      opt[1]	name	string	替换指定文件名文件
//	      or
//	      opt[1]	id	int	替换指定id文件
//	    mode=="list": r为包含多个文件路径的txt文件，按照Export mode=="all"输出的txt格式
//	    mode=="dir":  r为空
//	      opt[1]	dir	string	导入文件目录，按照Export导出的文件名进行匹配。若存在文件名则用文件名匹配，不存在则用ID匹配
//	Return error
func (p *Pak) Import(r io.Reader, mode string, value interface{}) error {

	var err error
	switch mode {
	case "file":
		if name, ok := value.(string); ok {
			err = p.Set(name, r)
		} else if id, ok := value.(int); ok {
			err = p.SetById(id, r)
		} else {
			glog.Fatalln("输入参数有误")
		}
	case "list":
		var fs *os.File
		var name, file string
		var id int

		scan := bufio.NewScanner(r)
		for scan.Scan() {
			line := scan.Text()
			param := strings.Split(line, ",")

			if len(param) != 2 {
				glog.Fatalln("pak.Import.list 输入文件格式有误")
			}
			file = param[1]
			fs, err = os.Open(file)
			if err != nil {
				glog.V(2).Infof("%v %s\n", err, file)
				continue
			}
			if strings.HasPrefix(param[0], "name") {
				name = param[0][5:]
				err = p.Set(name, fs)
			} else if strings.HasPrefix(param[0], "id") {
				id, err = strconv.Atoi(param[0][3:])
				if err != nil {
					return err
				}
				err = p.SetById(id, fs)
			} else {
				glog.Fatalln("pak.Import.list 输入文件格式有误")
			}
			if err != nil {
				return err
			}
			err = fs.Close()
			if err != nil {
				return err
			}
		}
		if err = scan.Err(); err != nil {
			glog.Fatalln("pak.Import.list 输入文件格式有误")
		}

	case "dir":
		var files []string
		var id int
		files, err = utils.GetDirFileList(value.(string))
		if err != nil {
			return err
		}
		for _, file := range files {
			name := path.Base(strings.ReplaceAll(file, "\\", "/"))
			fs, _ := os.Open(file)
			if p.CheckName(name) {
				err = p.Set(name, fs)
			} else {
				id, err = strconv.Atoi(name)
				if err != nil {
					glog.V(2).Infof("Skip File: %s\n", name)
					continue
				}
				if p.CheckId(id) {
					err = p.SetById(id, fs)
				} else {
					glog.V(2).Infof("Skip File: %s\n", name)
					continue
				}
			}
			if err != nil {
				glog.V(2).Infof("%v %s\n", err, file)
				return err
			}
			err = fs.Close()
			if err != nil {
				return err
			}
		}
	}
	return err
}
