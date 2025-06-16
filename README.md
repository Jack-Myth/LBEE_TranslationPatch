# LBEE_TranslationPatch  
## Little Busters English Edition 汉化程序

通过参考[LuckSystem](https://github.com/wetor/LuckSystem)和[lbee-utils](https://github.com/G2-Games/lbee-utils)编写  
感谢[lmr0420](https://github.com/lmr0420)协助捉虫  

![Preview](Preview/LBEE.jpg)  

可参考的翻译位于[TranslationText/LB_EX_Unicode](TranslationText/LB_EX_Unicode)  
待翻译的文本位于[TextMapping](TextMapping), 此文件夹下的Json会被程序导入到游戏内  
待翻译的图片位于[ImageMapping](ImageMapping)，此文件夹下的图片会被程序导入到游戏内，目前翻译了和剧情相关的图片文本。  
[Release页面](https://github.com/Jack-Myth/LBEE_TranslationPatch/releases)有一个或许对填写文本有帮助的小工具  

前往[Release页面](https://github.com/Jack-Myth/LBEE_TranslationPatch/releases)下载已编译的汉化程序  
打开LBEE_TranslationPatch.exe并选择LBEE的主程序即可执行汉化流程  
可以将项目里的TextMapping文件夹单独拿出来覆盖Release包里的同名文件夹来换成最新更新的翻译  
Linux(SteamDeck等环境)下可以将LBEE_TranslationPatch.exe作为非Steam游戏加入Steam并使用Proton9兼容层运行，文字可能有乱码，但逻辑应当能正确运行。

已知问题：  
- 为避免查看历史文本出现Bug，限制了选项的字库，部分选项显示为繁体中文。  

汉化可能存在疏漏或错误，如果有任何想要讨论的内容，欢迎提交Issue。

<details>
<summary>相关脚本对应的内容（可能有剧透）</summary>  

|脚本文件|内容|
|-------|----|
|_KEYWORD|BusterPedia关键字的解释
|_SAYAVOICE|应该是沙耶线小游戏的字幕
|_VARSTR|各种战斗文本，场景文本，角色等
|SEEN0513—SEEN0528|共通线
|SEEN1000—SEEN1004|小毬线
|SEEN1200—SEEN1203|佐佐美线
|SEEN2000—SEEN2004|铃线
|SEEN2005|恭介的一问一答
|SEEN2100|习得各种球时的对话
|SEEN2500|和真人玩的对话
|SEEN2513—SEEN2523|Refrain
|SEEN2600|踢罐子游戏
|SEEN2601|人偶剧演出
|SEEN2602|Refrain BadEnd
|SEEN2603|Refrain TrueEnd
|SEEN2800—SEEN2808|沙耶线（理树视角）
|SEEN2809|沙耶线（迷宫对话）
|SEEN2810—SEEN2888|沙耶线（沙耶视角）
|SEEN3000—SEEN3524|叶留佳线
|SEEN3600—SEEN3800|佳奈多线
|SEEN3900|叶留佳线 BadEnd
|SEEN4000—SEEN4103|库特线
|SEEN4444|（不清楚，某个总结剧情？）
|SEEN5000—SEEN5006|来谷线
|SEEN5522|练习时女生们的对话
|SEEN6000—SEEN6003|美鱼线
|SEEN6010|短歌比赛
|SEEN6100|科学部给美鱼配发新武器
|SEEN6101—SEEN6102|假面·齐藤对话
|SEEN6518|讨伐迷之生物
|SEEN8030|棒球比赛
|SEEN8110|棒球比赛选项
|SEEN8220|棒球比赛通知
|SEEN8250|（一句奇怪的话，不知道是什么）
|SEEN8580—SEEN8620|部分战斗提示
|SEEN8731—SEEN8737|战斗排位赛相关文本
|SEEN8750—SEEN9700|剧情战斗

上表中过滤了未解析出可翻译文本的脚本。  
</details>
