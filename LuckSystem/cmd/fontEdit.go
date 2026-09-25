/*
Copyright © 2022 NAME HERE <EMAIL ADDRESS>
*/
package cmd

import (
	"fmt"
	"image/png"
	"lucksystem/font"
	"os"

	"github.com/golang/glog"

	"github.com/spf13/cobra"
)

// fontEditCmd represents the fontEdit command
var fontEditCmd = &cobra.Command{
	Use:   "edit",
	Short: "修改或重构字体",
	Long:  `同时修改字体图像以及对应的info`,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("fontEdit called")
		if len(FontTTFInput) == 0 {
			fmt.Println("Error: required flag(s) \"input_ttf\" not set")
			return
		}
		f := font.LoadLucaFontFile(FontInfoSource, FontCzSource)
		out, err := os.Create(FontOutput)
		if err != nil {
			glog.Fatalln(err)
		}
		defer out.Close()
		ttf, err := os.Open(FontTTFInput)
		if err != nil {
			glog.Fatalln(err)
		}
		defer ttf.Close()
		if FontAppend {
			FontStartIndex = -1
		}
		err = f.Import(ttf, FontStartIndex, FontRedraw, FontCharsetInput)
		if err != nil {
			glog.Fatalln(err)
		}
		var outInfo *os.File = nil
		if len(FontInfoOutput) > 0 {
			outInfo, err = os.Create(FontInfoOutput)
			if err != nil {
				glog.Fatalln(err)
			}
			defer outInfo.Close()
		}

		if FontOutputCz {
			err = f.Write(out, outInfo)
		} else {
			if outInfo != nil {
				err = f.Info.Write(outInfo)
			}
			if err == nil {
				err = png.Encode(out, f.Image)
			}
		}
		if err != nil {
			glog.Fatalln(err)
		}

	},
}
var (
	FontTTFInput     string // ttf字体文件
	FontCharsetInput string // 替换或追加的字符集
	FontRedraw       bool   // 重绘
	FontAppend       bool   // 追加到最后
	FontStartIndex   int    // 替换或者重绘的序号，从零开始
	FontOutputCz     bool   // 直接输出CZ图像

)

func init() {
	fontCmd.AddCommand(fontEditCmd)
	fontEditCmd.Flags().StringVarP(&FontInfoOutput, "output_info", "O", "", "修改后字体info保存位置")

	fontEditCmd.Flags().StringVarP(&FontTTFInput, "input_ttf", "f", "", "绘制字符使用的TTF字体")
	fontEditCmd.Flags().StringVarP(&FontCharsetInput, "input_charset", "c", "", "增加或替换的字符集文本文件")
	fontEditCmd.Flags().BoolVarP(&FontAppend, "append", "a", false, "字符集绘制并添加到原字体最后")

	fontEditCmd.Flags().IntVarP(&FontStartIndex, "index", "i", 0, "字符集绘制并添加到的位置，从0开始")
	fontEditCmd.Flags().BoolVar(&FontOutputCz, "output_cz", false, "直接输出CZ图像并保留原图格式")
	fontEditCmd.Flags().BoolVarP(&FontRedraw, "redraw", "r", false, "重绘原字体图片")
	fontEditCmd.MarkFlagsMutuallyExclusive("append", "index")
}
