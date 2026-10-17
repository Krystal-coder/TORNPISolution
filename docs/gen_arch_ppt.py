# -*- coding: utf-8 -*-
"""
TORNPISolution 架构与开发指南 PPT 生成器。
运行： python docs/gen_arch_ppt.py
产物： docs/TORNPISolution架构与开发指南.pptx
维护： 改下方各 slide_* 函数里的文字即可，重新运行覆盖生成。
"""
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.enum.shapes import MSO_SHAPE
from pptx.oxml.ns import qn

# ---------------- 主题 ----------------
NAVY   = RGBColor(0x1B, 0x2A, 0x4A)   # 深蓝（标题/表头）
BLUE   = RGBColor(0x2E, 0x5C, 0x8A)   # 中蓝
TEAL   = RGBColor(0x12, 0x8C, 0x9B)   # 青（强调）
GREEN  = RGBColor(0x2E, 0x7D, 0x32)   # 绿（稳定/正确）
AMBER  = RGBColor(0xB8, 0x86, 0x0B)   # 琥珀（注意）
DARK   = RGBColor(0x22, 0x2A, 0x35)   # 正文
GRAY   = RGBColor(0x5A, 0x66, 0x72)   # 次要
LIGHT  = RGBColor(0xEE, 0xF2, 0xF7)   # 浅底
LIGHT2 = RGBColor(0xF7, 0xF9, 0xFB)
WHITE  = RGBColor(0xFF, 0xFF, 0xFF)
CODEBG = RGBColor(0x1E, 0x1E, 0x2E)   # 代码底
CODEFG = RGBColor(0xE6, 0xED, 0xF3)
FONT   = "Microsoft YaHei"
MONO   = "Consolas"

prs = Presentation()
prs.slide_width  = Inches(13.333)
prs.slide_height = Inches(7.5)
BLANK = prs.slide_layouts[6]
SW, SH = 13.333, 7.5


# ---------------- 基础辅助 ----------------
def _style(run, size=18, bold=False, color=DARK, name=FONT, italic=False):
    run.font.size = Pt(size)
    run.font.bold = bold
    run.font.italic = italic
    run.font.color.rgb = color
    run.font.name = name
    rPr = run._r.get_or_add_rPr()
    for tag in ("a:ea", "a:cs"):
        el = rPr.find(qn(tag))
        if el is None:
            el = rPr.makeelement(qn(tag), {})
            rPr.append(el)
        el.set("typeface", name)


def slide():
    return prs.slides.add_slide(BLANK)


def rect(s, l, t, w, h, fill=None, line=None, line_w=1.0, shape=MSO_SHAPE.RECTANGLE):
    sp = s.shapes.add_shape(shape, Inches(l), Inches(t), Inches(w), Inches(h))
    if fill is None:
        sp.fill.background()
    else:
        sp.fill.solid(); sp.fill.fore_color.rgb = fill
    if line is None:
        sp.line.fill.background()
    else:
        sp.line.color.rgb = line; sp.line.width = Pt(line_w)
    sp.shadow.inherit = False
    return sp


def text(s, l, t, w, h, runs, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.TOP,
         space_after=4, line_spacing=1.0):
    """runs: list of paragraphs; each paragraph = list of (txt,size,bold,color,name) tuples"""
    tb = s.shapes.add_textbox(Inches(l), Inches(t), Inches(w), Inches(h))
    tf = tb.text_frame
    tf.word_wrap = True
    tf.vertical_anchor = anchor
    for i, para in enumerate(runs):
        p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        p.alignment = align
        p.space_after = Pt(space_after)
        p.line_spacing = line_spacing
        for seg in para:
            r = p.add_run(); r.text = seg[0]
            _style(r, seg[1] if len(seg) > 1 else 18,
                   seg[2] if len(seg) > 2 else False,
                   seg[3] if len(seg) > 3 else DARK,
                   seg[4] if len(seg) > 4 else FONT)
    return tb


def box_text(sp, runs, align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE):
    tf = sp.text_frame
    tf.word_wrap = True
    tf.vertical_anchor = anchor
    tf.margin_left = Inches(0.08); tf.margin_right = Inches(0.08)
    tf.margin_top = Inches(0.03); tf.margin_bottom = Inches(0.03)
    for i, para in enumerate(runs):
        p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        p.alignment = align
        p.space_after = Pt(2); p.line_spacing = 1.0
        for seg in para:
            r = p.add_run(); r.text = seg[0]
            _style(r, seg[1] if len(seg) > 1 else 14,
                   seg[2] if len(seg) > 2 else False,
                   seg[3] if len(seg) > 3 else WHITE,
                   seg[4] if len(seg) > 4 else FONT)


def down_arrow(s, cx, t, h=0.22, color=TEAL):
    rect(s, cx - 0.14, t, 0.28, h, fill=color, shape=MSO_SHAPE.DOWN_ARROW)


def right_arrow(s, l, cy, w=0.3, color=TEAL):
    rect(s, l, cy - 0.13, w, 0.26, fill=color, shape=MSO_SHAPE.RIGHT_ARROW)


def title_bar(s, title, subtitle=None, accent=TEAL):
    rect(s, 0, 0, SW, 1.12, fill=NAVY)
    rect(s, 0, 1.12, SW, 0.06, fill=accent)
    text(s, 0.55, 0.14, SW - 1.1, 0.62, [[(title, 27, True, WHITE)]],
         anchor=MSO_ANCHOR.MIDDLE)
    if subtitle:
        text(s, 0.57, 0.74, SW - 1.1, 0.34, [[(subtitle, 13, False, RGBColor(0xC8, 0xD6, 0xE5))]],
             anchor=MSO_ANCHOR.MIDDLE)


def footer(s, idx):
    text(s, SW - 1.3, SH - 0.42, 1.0, 0.3, [[(str(idx), 11, False, GRAY)]],
         align=PP_ALIGN.RIGHT)
    text(s, 0.55, SH - 0.42, 6.0, 0.3, [[("TORNPISolution 架构与开发指南", 10, False, RGBColor(0x9A, 0xA6, 0xB2))]])


def table(s, l, t, w, col_w, header, rows, row_h=0.42, head_h=0.5,
          fsize=12.5, hsize=13, head_fill=NAVY, zebra=True, aligns=None):
    ncol = len(header)
    nrow = len(rows) + 1
    gf = s.shapes.add_table(nrow, ncol, Inches(l), Inches(t), Inches(w), Inches(head_h + row_h * len(rows)))
    tbl = gf.table
    tbl.first_row = False
    tbl.horz_banding = False
    for i, cw in enumerate(col_w):
        tbl.columns[i].width = Inches(cw)
    tbl.rows[0].height = Inches(head_h)
    for r in range(1, nrow):
        tbl.rows[r].height = Inches(row_h)
    # header
    for c, htxt in enumerate(header):
        cell = tbl.cell(0, c)
        _fill_cell(cell, htxt, hsize, True, WHITE, head_fill, PP_ALIGN.CENTER)
    # body
    for r, row in enumerate(rows, start=1):
        for c, val in enumerate(row):
            bg = LIGHT2 if (zebra and r % 2 == 0) else WHITE
            al = PP_ALIGN.LEFT
            if aligns and aligns[c] == "c":
                al = PP_ALIGN.CENTER
            bold = (c == 0)
            col = NAVY if c == 0 else DARK
            _fill_cell(tbl.cell(r, c), val, fsize, bold, col, bg, al)
    return tbl


def _fill_cell(cell, txt, size, bold, color, fill, align):
    cell.fill.solid(); cell.fill.fore_color.rgb = fill
    cell.vertical_anchor = MSO_ANCHOR.MIDDLE
    cell.margin_left = Inches(0.09); cell.margin_right = Inches(0.07)
    cell.margin_top = Inches(0.03); cell.margin_bottom = Inches(0.03)
    tf = cell.text_frame
    tf.word_wrap = True
    lines = txt.split("\n")
    for i, ln in enumerate(lines):
        p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        p.alignment = align
        p.space_after = Pt(1); p.line_spacing = 1.0
        r = p.add_run(); r.text = ln
        _style(r, size, bold, color)


def code_panel(s, l, t, w, h, code_lines, title=None, fsize=12.5):
    rect(s, l, t, w, h, fill=CODEBG, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    yy = t + 0.12
    if title:
        text(s, l + 0.2, yy, w - 0.4, 0.3, [[(title, 12, True, RGBColor(0x7F, 0xD4, 0xFF), MONO)]])
        yy += 0.34
    tb = s.shapes.add_textbox(Inches(l + 0.2), Inches(yy), Inches(w - 0.4), Inches(h - (yy - t) - 0.12))
    tf = tb.text_frame; tf.word_wrap = True
    for i, ln in enumerate(code_lines):
        p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        p.space_after = Pt(1); p.line_spacing = 1.02
        segs = ln if isinstance(ln, list) else [(ln, fsize, False, CODEFG, MONO)]
        for seg in segs:
            r = p.add_run(); r.text = seg[0]
            _style(r, seg[1] if len(seg) > 1 else fsize,
                   seg[2] if len(seg) > 2 else False,
                   seg[3] if len(seg) > 3 else CODEFG,
                   seg[4] if len(seg) > 4 else MONO)


def bullets(s, l, t, w, h, items, size=15, gap=6):
    """items: list of (level, text, bold, color) ; level 0 = ●, 1 = –"""
    tb = s.shapes.add_textbox(Inches(l), Inches(t), Inches(w), Inches(h))
    tf = tb.text_frame; tf.word_wrap = True
    first = True
    for it in items:
        lvl, txt = it[0], it[1]
        bold = it[2] if len(it) > 2 else False
        color = it[3] if len(it) > 3 else DARK
        p = tf.paragraphs[0] if first else tf.add_paragraph()
        first = False
        p.space_after = Pt(gap); p.line_spacing = 1.06
        p.level = lvl
        mark = "●  " if lvl == 0 else "–  "
        r0 = p.add_run(); r0.text = mark
        _style(r0, size if lvl == 0 else size - 1, True, TEAL if lvl == 0 else GRAY)
        # support inline bold via ** markers
        _emit_bold_segments(p, txt, size if lvl == 0 else size - 1, bold, color)
    return tb


def _emit_bold_segments(p, txt, size, bold, color):
    parts = txt.split("**")
    for i, seg in enumerate(parts):
        if seg == "":
            continue
        r = p.add_run(); r.text = seg
        _style(r, size, bold or (i % 2 == 1), NAVY if (i % 2 == 1) else color)


# ================= 幻灯片 =================
def s01_cover():
    s = slide()
    rect(s, 0, 0, SW, SH, fill=NAVY)
    rect(s, 0, 0, 0.28, SH, fill=TEAL)
    rect(s, 0.9, 4.62, 5.2, 0.05, fill=TEAL)
    text(s, 0.95, 2.15, 11.4, 1.5, [[("TORNPISolution", 52, True, WHITE)],
                                     [("架构与开发指南", 40, True, RGBColor(0x8F, 0xD6, 0xDE))]],
         space_after=6)
    text(s, 0.98, 4.8, 11.0, 1.2,
         [[("面向协作开发者：分层结构 · 调试入口 · 三类扩展 · 稳定核心", 18, False, RGBColor(0xD5, 0xDE, 0xE8))],
          [("血压仪产测上位机  |  .NET 8  WPF  |  6 工程分层  |  MVVM + 依赖注入", 14, False, RGBColor(0x9F, 0xB2, 0xC6))]],
         space_after=8)


def s02_agenda():
    s = slide()
    title_bar(s, "这份文档能帮你", "读完就能独立定位问题、照步骤扩展功能")
    items = [
        (0, "看懂 **6 个工程**怎么分层、谁依赖谁（含依赖方向图）"),
        (0, "搞清楚一次测试怎么从**「点运行」跑到「界面出结果」**（执行流）"),
        (0, "出问题时，直接对号入座**该在哪个文件下断点**（调试速查表）"),
        (0, "三类常见扩展**照着抄**：加测试模块 / 加设备 / 设动态规格"),
        (0, "认清哪些是**「稳定核心」**基本不用动，避免误改踩坑"),
        (0, "记住几条**协作约定与已知的坑**（串口互斥、线程、项名匹配…）"),
    ]
    bullets(s, 0.9, 1.7, 11.5, 4.6, items, size=19, gap=16)
    rect(s, 0.9, 6.15, 11.5, 0.75, fill=LIGHT, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s, 1.15, 6.24, 11.0, 0.6,
         [[("心智模型：  ", 15, True, TEAL),
           ("Spec 定义测什么 → TestItem 执行怎么测 → TestItemResult 装结果 → MainViewModel 调度 → UI 判定显示；中间靠「项名」和「DI 契约」解耦。", 14, False, DARK)]],
         anchor=MSO_ANCHOR.MIDDLE)
    footer(s, 2)


def s03_layers():
    s = slide()
    title_bar(s, "全景分层与依赖方向", "上层依赖下层；Interface 是契约中枢；Share 谁都能依赖、谁都不依赖别人")
    L, W = 1.5, 8.0
    # UI
    b = rect(s, L, 1.55, W, 0.92, fill=BLUE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    box_text(b, [[("TORNPISolution  (WPF UI + 调度)", 17, True, WHITE)],
                 [("App.xaml.cs=DI组合根 · MainViewModel=调度 · Views/ViewModels", 11.5, False, RGBColor(0xD6, 0xE2, 0xEF))]])
    down_arrow(s, L + W / 2, 2.5)
    # Service
    b = rect(s, L, 2.78, W, 0.92, fill=TEAL, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    box_text(b, [[("TORNPISolution.Service  (业务 + 设备实现)", 17, True, WHITE)],
                 [("Spec=模块目录 · TestItem=执行 · Devices=10个设备驱动 · Config/Log", 11.5, False, RGBColor(0xDD, 0xF2, 0xF4))]])
    down_arrow(s, L + W / 2, 3.73)
    # 契约层三块
    y = 4.0
    specs = [(L, 2.5, "Interface", "服务/设备契约(11个接口)", BLUE),
             (L + 2.75, 2.5, "GoogolEtherCAT", "运动控制 GENControl/气缸", RGBColor(0x4A, 0x6F, 0xA5)),
             (L + 5.5, 2.5, "Data", "EF Core 持久化(预留·未接主流程)", RGBColor(0x6B, 0x7B, 0x8C))]
    for x, w, nm, desc, col in specs:
        bb = rect(s, x, y, w, 0.92, fill=col, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
        box_text(bb, [[(nm, 15, True, WHITE)], [(desc, 10.5, False, RGBColor(0xE2, 0xE8, 0xF0))]])
    down_arrow(s, L + W / 2, 4.98)
    # Share
    b = rect(s, L, 5.26, W, 0.82, fill=NAVY, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    box_text(b, [[("TORNPISolution.Share  (共享模型 — 最底层)", 16, True, WHITE)],
                 [("TestItemModel/TestModuleModel/TestModuleIds/TestItemResult/枚举/配置模型", 11, False, RGBColor(0xC8, 0xD6, 0xE5))]])
    # 右侧说明
    rect(s, 9.95, 1.55, 2.85, 4.53, fill=LIGHT, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s, 10.15, 1.7, 2.5, 4.3,
         [[("依赖规则", 15, True, NAVY)],
          [("① 只能上层→下层依赖", 12.5, False, DARK)],
          [("② 跨层调用一律走 Interface 契约，不 new 具体实现", 12.5, False, DARK)],
          [("③ 所有服务在 App.xaml.cs 组合根统一注册（DI）", 12.5, False, DARK)],
          [("④ Share 是纯数据/模型，放谁都依赖的东西", 12.5, False, DARK)],
          [("⑤ Data 目前只依赖 Share、未被主程序引用（持久化预留）", 12.5, False, AMBER)]],
         space_after=8)
    footer(s, 3)


def s04_roles():
    s = slide()
    title_bar(s, "各层职责速查", "每个工程放什么、你会在这里改什么")
    header = ["工程", "角色", "关键内容", "你通常在这改什么"]
    col_w = [2.55, 1.9, 5.0, 2.75]
    rows = [
        ["Share", "共享模型\n(最底层)", "TestItemModel · TestModuleModel · TestModuleIds · TestItemResult · TestResultStatus · InstrumentConnectionConfig", "加模块 Id、加结果字段、加枚举值"],
        ["Interface", "服务/设备契约", "ITestItemService · ITestModuleService · IProductController · IBloodPressureSimulator · IMassFlowController · INfcReader …（共 11 个接口）", "加新设备接口"],
        ["Service", "业务 + 设备实现", "Specs/Spec.cs(目录) · SpecItems.cs(项名) · TestExecution/(TestItem 分派+模块) · Devices/(10 设备) · InstrumentConfigService · LogService", "加模块实现、加设备驱动、改规格"],
        ["GoogolEtherCAT", "运动控制", "GENControl(固高 EtherCAT) · CylinderEtherCAT(气缸) · ConvertHelper", "气缸/IO 动作、点位"],
        ["Data", "EF Core 持久化\n(预留·未接入)", "TornpiDbContext", "结果入库（当前未接主流程）"],
        ["TORNPISolution", "WPF UI + 调度", "App.xaml.cs(DI 组合根) · MainViewModel(调度) · TestItemViewModel(判定显示) · Views/(MainWindow 等)", "界面布局、调度、绑定"],
    ]
    table(s, 0.6, 1.5, 12.2, col_w, header, rows, row_h=0.82, head_h=0.46,
          fsize=10.5, hsize=13, aligns=["l", "c", "l", "l"])
    footer(s, 4)


def s05_flow():
    s = slide()
    title_bar(s, "一次测试怎么跑起来", "从点「运行」到界面出结果的完整链路")
    steps = [
        ("点「运行」→ MainViewModel.RunPlan（UI 线程）", "RefreshModuleCatalog + Rebuild（跟随最新配置）→ BeginPlanRun 复位 → 建 CancellationTokenSource", BLUE),
        ("for 每个模块 → Task.Run（切后台线程）", "调用 TestItem.TestItemxx(seq, module, onItemCompleted, token)；UI 线程完全空闲不卡", TEAL),
        ("ResolveModule(module.Id) 分派 → 具体模块方法", "如 M05 → BpStaticAccuracyModule；RunModuleAsync 统一包日志/异常隔离", RGBColor(0x2E, 0x7D, 0x32)),
        ("await collector.SetAsync(项名, TestItemResult)", "每写一项自动触发 onItemCompleted 回调 → 边测边回填，不等整模块跑完", RGBColor(0xB8, 0x86, 0x0B)),
        ("Dispatcher → TestItemViewModel.ApplyResult（判定+显示）", "模块跑完：最终扫描，回调未覆盖且非「不判定」的项 → 强制判 FAIL", RGBColor(0x8E, 0x44, 0xAD)),
    ]
    y = 1.5
    for i, (h, d, col) in enumerate(steps):
        b = rect(s, 0.7, y, 8.0, 0.86, fill=col, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
        box_text(b, [[(f"{i+1}.  {h}", 13.5, True, WHITE)], [(d, 10.8, False, RGBColor(0xEC, 0xF1, 0xF6))]],
                 align=PP_ALIGN.LEFT)
        if i < len(steps) - 1:
            down_arrow(s, 4.7, y + 0.88, h=0.16)
        y += 1.045
    # 右侧关键点
    rect(s, 9.0, 1.5, 3.8, 5.1, fill=LIGHT, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s, 9.22, 1.66, 3.4, 4.9,
         [[("关键点", 16, True, NAVY)],
          [("▪ 模块在后台线程跑，UI 不卡顿", 12.5, False, DARK)],
          [("▪ 逐项回调，边测边显示结果", 12.5, False, DARK)],
          [("▪ 单模块失败不中断，全测完再汇总（任一项 FAIL → 整体 NG）", 12.5, False, DARK)],
          [("▪ 点「停止」= 取消令牌，当前模块尽快退出、后续不再执行", 12.5, False, DARK)],
          [("▪ 结果按「项名」匹配回填，与顺序无关", 12.5, False, DARK)],
          [("▪ 不判定项（演示/预留）保持「未测试」，不强制 FAIL", 12.5, False, DARK)]],
         space_after=9)
    footer(s, 5)


def s06_contract():
    s = slide()
    title_bar(s, "核心数据契约（三件套）", "Spec 定义「测什么」，TestItemResult 装「测得什么」，UI 判定")
    cards = [
        ("Spec  (ITestModuleService)", "定义测什么", BLUE,
         ["模块目录 GetAllModules()", "每模块一个 Build 方法", "AddTI 建模块 / AddResult 加项", "写的是【静态规格】= 默认兜底"]),
        ("TestItemModel", "一条测试项定义", TEAL,
         ["Name/Description/Unit", "LowerLimit/UpperLimit", "ValueSource（决定显示列）", "ShowWaveform（行内波形）"]),
        ("TestItemResult", "一次执行的实测结果", GREEN,
         ["Value（判定主值）", "SpecMin/SpecMax（动态规格）", "InstrumentValue / ProductValue", "InstrumentText / ProductText", "Points / NotJudged"]),
    ]
    x = 0.6
    for nm, sub, col, lines in cards:
        hd = rect(s, x, 1.5, 4.0, 0.78, fill=col, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
        box_text(hd, [[(nm, 15, True, WHITE)], [(sub, 11, False, RGBColor(0xE6, 0xEE, 0xF5))]])
        rect(s, x, 2.34, 4.0, 2.2, fill=LIGHT2, line=col, line_w=1.2, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
        text(s, x + 0.22, 2.5, 3.6, 1.95,
             [[("· " + ln, 12.5, False, DARK)] for ln in lines], space_after=7)
        x += 4.11
    # 关系说明
    rect(s, 0.6, 4.75, 12.2, 1.95, fill=LIGHT, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s, 0.85, 4.9, 11.7, 1.75,
         [[("三者关系", 15, True, NAVY)],
          [("① Spec 定义测试项清单 + 静态规格（默认/兜底）；② TestItem 运行时产生【实测值 + 本次动态规格】装进 TestItemResult 回传；", 13, False, DARK)],
          [("③ UI 判定：有动态规格用动态，否则用 Spec 静态；结果按【项名】匹配回填，与顺序无关。", 13, False, DARK)],
          [("⚠ 项名拼错 → 运行时 Warn 日志 +「结果项不在 Spec 定义中」+ 该项默认 0 判 FAIL（防静默错误）。项名统一引用 SpecItems 常量。", 12.5, True, AMBER)]],
         space_after=6)
    footer(s, 6)


def s07_debug():
    s = slide()
    title_bar(s, "调试指南：出问题在哪下断点", "按现象对号入座，直接定位到文件与方法")
    header = ["要调什么 / 现象", "去这个文件", "关键方法 / 位置"]
    col_w = [4.4, 4.3, 3.5]
    rows = [
        ["整体顺序 / 停止 / 汇总 / 界面卡顿", "ViewModels/MainViewModel.cs", "RunPlan"],
        ["某模块测试流程不对（加压/回读/判定值）", "TestExecution/TestItem.<域>.cs\n(Bp / Aecg / NfcCharging)", "<Module>Module"],
        ["模块没被调用 / 分派到错误方法", "TestExecution/Core/TestItem.cs", "ResolveModule 的 switch"],
        ["项名 / 规格 / 目录 / 少项多项", "Specs/Spec.cs + Specs/SpecItems.cs", "Build* / AddResult / 常量"],
        ["PASS-FAIL 判定 / 显示列不对", "ViewModels/TestItemViewModel.cs", "ApplyResult / JudgeItem"],
        ["产品串口收发 / 帧解析（0x7E/0x73…）", "Devices/ProductController.cs\n(+ Models/ProductProtocol.cs)", "各 *Async / Parse*"],
        ["模拟仪动作（康伟达 / BPA700）", "Devices/KangwendaNIBPSimulator.cs\n/ BPA700Simulator.cs", "对应指令方法"],
        ["配置改了不生效", "InstrumentConfigService.cs\n+ Config/instruments.json", "Load"],
        ["波形不显示 / 不刷新", "TestItem.PressureSampled →\nMainViewModel → TestItemViewModel.WaveformSamples", "事件链路"],
        ["串口打开卡死整机", "Communication/SerialPortOpener.cs", "OpenAsync（后台限时）"],
    ]
    table(s, 0.6, 1.45, 12.2, col_w, header, rows, row_h=0.5, head_h=0.44,
          fsize=11, hsize=12.5, aligns=["l", "l", "l"])
    footer(s, 7)


def s08_ext_module():
    s = slide()
    title_bar(s, "扩展①：新增一个测试模块（以 M17 为例）", "照抄现有模块，5 步搞定；界面会自动多出一个可拖拽模块", accent=GREEN)
    steps = [
        ("1", "TestModuleIds.cs", "加 Id 常量", 'public const string XxxTest = "M17";'),
        ("2", "SpecItems.cs", "加项名常量", 'public const string XxxItem = "XXX-001 名称";'),
        ("3", "Spec.cs", "写 BuildXxx()：AddTI + AddResult，并加进 GetAllModules 列表", "见右侧骨架"),
        ("4", "新建 TestItem.Xxx.cs", "partial class TestItem，写 XxxModule(collector, token)", "见右侧骨架"),
        ("5", "TestItem.cs → ResolveModule", "switch 加一行分派", "TestModuleIds.XxxTest => (nameof(XxxModule), () => XxxModule(collector, token), null),"),
    ]
    y = 1.5
    for n, f, act, code in steps:
        rect(s, 0.6, y, 0.42, 0.62, fill=GREEN, shape=MSO_SHAPE.OVAL)
        bb = s.shapes[-1]; box_text(bb, [[(n, 15, True, WHITE)]])
        rect(s, 1.12, y, 5.7, 0.62, fill=LIGHT2, line=RGBColor(0xD0, 0xD8, 0xE0), line_w=0.75, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
        text(s, 1.3, y + 0.05, 5.4, 0.55,
             [[(f + "  ", 12.5, True, NAVY), (act, 11.5, False, DARK)]], anchor=MSO_ANCHOR.MIDDLE)
        y += 0.72
    code_panel(s, 7.0, 1.5, 5.75, 2.9, [
        [("// Spec.cs", 11.5, False, RGBColor(0x6A, 0x99, 0x55), MONO)],
        [("private static TestModuleModel ", 11.5, False, CODEFG, MONO), ("BuildXxx", 11.5, True, RGBColor(0xDC, 0xD6, 0x7D), MONO), ("()", 11.5, False, CODEFG, MONO)],
        [("{", 11.5, False, CODEFG, MONO)],
        [("  var ti = AddTI(TestModuleIds.XxxTest,", 11.5, False, CODEFG, MONO)],
        [('    "模块名", "\\uE9XX", "模块说明");', 11.5, False, CODEFG, MONO)],
        [("  AddResult(ti, name: SpecItems.XxxItem,", 11.5, False, CODEFG, MONO)],
        [('    description: "…", specMin: 0, specMax: 1,', 11.5, False, CODEFG, MONO)],
        [('    unit: "", valueSource: "Product");', 11.5, False, CODEFG, MONO)],
        [("  return ti;", 11.5, False, CODEFG, MONO)],
        [("}", 11.5, False, CODEFG, MONO)],
    ], title="Spec 骨架")
    code_panel(s, 7.0, 4.45, 5.75, 2.5, [
        [("// TestItem.Xxx.cs", 10.5, False, RGBColor(0x6A, 0x99, 0x55), MONO)],
        [("public partial class TestItem", 10.5, False, CODEFG, MONO)],
        [("{", 10.5, False, CODEFG, MONO)],
        [("  private async Task ", 10.5, False, CODEFG, MONO), ("XxxModule", 10.5, True, RGBColor(0xDC, 0xD6, 0x7D), MONO), ("(", 10.5, False, CODEFG, MONO)],
        [("      TestItemResultCollector c, CancellationToken t)", 10.5, False, CODEFG, MONO)],
        [("  {", 10.5, False, CODEFG, MONO)],
        [("    await c.SetAsync(SpecItems.XxxItem,", 10.5, False, CODEFG, MONO)],
        [("        TestItemResult.Of(value));", 10.5, False, CODEFG, MONO)],
        [("  }", 10.5, False, CODEFG, MONO)],
        [("}", 10.5, False, CODEFG, MONO)],
    ], title="模块骨架")
    rect(s, 0.6, 5.2, 6.22, 1.45, fill=RGBColor(0xE8, 0xF5, 0xE9), shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s, 0.82, 5.32, 5.8, 1.25,
         [[("✅ 不用改：", 13.5, True, GREEN)],
          [("MainViewModel（调度）· TestItemResultCollector · TestItemResult · 任何 XAML。", 12.5, False, DARK)],
          [("新模块自动出现在模块库，可拖进方案、可保存/加载。", 12, False, GRAY)]],
         space_after=5)
    footer(s, 8)


def s09_ext_device():
    s = slide()
    title_bar(s, "扩展②：新增一个设备", "两种情况：复用已有契约 / 全新设备类型", accent=GREEN)
    # A
    rect(s, 0.6, 1.5, 6.05, 0.6, fill=BLUE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    box_text(s.shapes[-1], [[("A. 复用已有契约（如再加一台血压模拟仪）", 14, True, WHITE)]], align=PP_ALIGN.LEFT)
    a = [
        (0, "Devices/ 新建 **XxxSimulator : IBloodPressureSimulator**，实现接口方法", False),
        (0, "Share 的 **SimulatorType 枚举加值** + InstrumentConnectionConfig 加配置节点", False),
        (0, "**BloodPressureSimulatorSession.CreateSimulator** 的 switch 加分支", False),
        (0, "配置页加该设备参数编辑（可选）", False),
    ]
    bullets(s, 0.75, 2.2, 5.8, 2.2, a, size=13, gap=8)
    code_panel(s, 0.6, 4.35, 6.05, 1.15, [
        [("CreateSimulator: config.ActiveSimulator switch {", 11, False, CODEFG, MONO)],
        [("  Kangwenda => new KangwendaNIBPSimulator(...),", 11, False, CODEFG, MONO)],
        [("  BPA700    => new BPA700Simulator(...),", 11, False, CODEFG, MONO)],
        [("  XxxType   => new XxxSimulator(...),   // ← 加这里", 11, True, RGBColor(0x7F, 0xD4, 0xFF), MONO)],
    ])
    # B
    rect(s, 6.85, 1.5, 5.9, 0.6, fill=TEAL, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    box_text(s.shapes[-1], [[("B. 全新设备类型（如新串口仪表）", 14, True, WHITE)]], align=PP_ALIGN.LEFT)
    bitems = [
        (0, "Interface/ 新建 **IXxx**（定义能力，只放契约）", False),
        (0, "Devices/ 新建 **XxxDevice : IXxx**（实现串口/SDK 通讯）", False),
        (0, "App.xaml.cs **ConfigureServices 注册**（⚠ 生命周期）", False),
        (1, "每次新开串口 → 用**工厂 Func<IXxx>**（绝不能单例）", False),
        (1, "全局共享连接 → 用**单例 AddSingleton**", False),
        (0, "在需要的模块方法里通过 DI/工厂取用", False),
    ]
    bullets(s, 7.0, 2.2, 5.6, 3.3, bitems, size=13, gap=8)
    code_panel(s, 6.85, 5.55, 5.9, 1.1, [
        [("// App.xaml.cs 组合根", 11, False, RGBColor(0x6A, 0x99, 0x55), MONO)],
        [("services.AddSingleton<Func<IXxx>>(sp => {", 11, False, CODEFG, MONO)],
        [("  var cfg = sp.GetRequiredService<IInstrumentConfigService>();", 11, False, CODEFG, MONO)],
        [("  return () => new XxxDevice(cfg.Load().Xxx); });", 11, False, CODEFG, MONO)],
    ])
    rect(s, 0.6, 5.65, 6.05, 1.0, fill=RGBColor(0xE8, 0xF5, 0xE9), shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s, 0.82, 5.74, 5.7, 0.85,
         [[("✅ 不用改：", 13, True, GREEN), ("TestItem 分派表、MainViewModel 调度。", 12.5, False, DARK)],
          [("设备通过接口被模块方法调用，换实现不影响上层。", 11.5, False, GRAY)]],
         space_after=4, anchor=MSO_ANCHOR.MIDDLE)
    footer(s, 9)


def s10_dynamic_spec():
    s = slide()
    title_bar(s, "扩展③：设置动态规格", "静态规格是兜底；动态规格运行时按仪器读回值现算，UI 判定优先用它", accent=GREEN)
    # 左：三种构造
    text(s, 0.6, 1.4, 6.2, 0.4, [[("TestItemResult 的几种回传方式", 15, True, NAVY)]])
    rows = [
        ("Of(value)", "普通项：只有测试值，按 Spec 静态规格判定"),
        ("OfText / OfProductText", "文本项：主值判 1/0，仪器列/产品列显示读回文本"),
        ("Demo(text)", "演示不判定：保持「未测试」，不参与合格判定"),
        ("WithDynamicSpec(v, min, max)", "动态规格：主值 + 本次现算的上下限"),
        ("WithDualValues(inst, prod, min, max)", "双值比对：主判定=产品值，规格=仪器值±容差"),
    ]
    y = 1.85
    for sig, desc in rows:
        rect(s, 0.6, y, 6.2, 0.66, fill=LIGHT2, line=RGBColor(0xD0, 0xD8, 0xE0), line_w=0.75, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
        text(s, 0.78, y + 0.04, 5.9, 0.6,
             [[(sig, 12, True, TEAL, MONO)], [(desc, 11, False, DARK)]], space_after=1, anchor=MSO_ANCHOR.MIDDLE)
        y += 0.74
    # 右：例子 + ValueSource
    code_panel(s, 7.0, 1.4, 5.75, 1.95, [
        [("// 例：血压精度项，规格 = 仪器读回值 ±2", 11.5, False, RGBColor(0x6A, 0x99, 0x55), MONO)],
        [("var inst = await ReadInstrumentAsync();", 11.5, False, CODEFG, MONO)],
        [("var prod = await ReadProductAsync();", 11.5, False, CODEFG, MONO)],
        [("await collector.SetAsync(SpecItems.Bp004Sys,", 11.5, False, CODEFG, MONO)],
        [("  TestItemResult.WithDualValues(", 11.5, False, CODEFG, MONO)],
        [("    inst, prod, inst - 2, inst + 2));", 11.5, True, RGBColor(0x7F, 0xD4, 0xFF), MONO)],
    ], title="动态规格示例")
    text(s, 7.0, 3.42, 5.75, 0.4, [[("ValueSource（TestItemModel，决定 UI 显示列）", 14, True, NAVY)]])
    vs = [
        ("Instrument", "默认 → 显示在「仪器测试值」列"),
        ("Product", "显示在「产品上报值」列"),
        ("Both", "仪器值 + 产品值 双列（精度比对）"),
        ("Error", "主值=误差按静态规格判，双列显仪器/产品"),
        ("Points", "逐点判定，主值=最差点误差"),
    ]
    y = 3.82
    for k, v in vs:
        text(s, 7.05, y, 5.7, 0.36, [[("· " + k + "：", 12, True, BLUE, MONO), (v, 11.5, False, DARK)]])
        y += 0.36
    rect(s, 0.6, 5.7, 12.15, 1.0, fill=RGBColor(0xFF, 0xF8, 0xE1), shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s, 0.82, 5.8, 11.7, 0.85,
         [[("动态「项清单」（一点一项，M05 BP-003）：", 13, True, AMBER),
           ("Spec 注入 IInstrumentConfigService，按当前勾选的压力点用 SpecItems.Bp003Point(v) 生成项名。", 12.5, False, DARK)],
          [("配置改点无需重启 —— RunPlan 开始会 RefreshModuleCatalog + Rebuild，保证运行判定与界面项清单同源。", 12, False, DARK)]],
         space_after=4, anchor=MSO_ANCHOR.MIDDLE)
    footer(s, 10)


def s11_stable():
    s = slide()
    title_bar(s, "稳定核心：这些基本不用动", "新增功能优先「加分模块 / 加设备 / 加项」，而不是改这些", accent=GREEN)
    header = ["类 / 文件", "职责", "为什么不用动"]
    col_w = [4.0, 3.0, 5.2]
    rows = [
        ["App.xaml.cs", "DI 组合根", "只有加「新服务」时才动注册；其余代码别自己 new 服务实现"],
        ["MainViewModel.RunPlan", "通用调度循环", "模块无关；加模块/改顺序都不用改它"],
        ["TestItem.TestItemxx + RunModuleAsync", "通用执行包装", "日志/异常隔离已统一；模块方法只写测试流程本身"],
        ["TestItemResultCollector", "结果收集 + 逐项回调", "通用机制，SetAsync 自动触发 UI 回填"],
        ["TestItemResult / TestItemModel / TestModuleModel", "数据契约", "改它=动全身；加字段要全链路评估，谨慎"],
        ["TestItemViewModel.ApplyResult / JudgeItem", "判定 + 显示", "「动态规格优先」逻辑已通用，各模块共用"],
        ["MainWindow.xaml（DataGrid）", "结果表格", "列绑定模块无关，自动适配任意模块的项"],
    ]
    table(s, 0.6, 1.5, 12.2, col_w, header, rows, row_h=0.62, head_h=0.46,
          fsize=11.5, hsize=13, aligns=["l", "l", "l"])
    footer(s, 11)


def s12_conventions():
    s = slide()
    title_bar(s, "协作约定与已知的坑", "踩过、修过的，写在这避免重复踩")
    left = [
        (0, "**串口是独占资源**：同一串口任意时刻只能一个持有者。ModbusRtuClient 每次操作 Transaction 抢锁；产品串口测量期间 ProductController 自持，采集间隙主动让出供 Modbus 插入读压力。", False),
        (0, "**后台线程 vs UI**：模块在 Task.Run 后台跑；回传 UI 必须 Dispatcher.Invoke；PressureSampled 在后台线程触发，UI 侧节流。", False),
        (0, "**项名是唯一钥匙**：Spec 与 TestItem 必须引用同一 SpecItems 常量；拼错→Warn + 判 FAIL。", False),
    ]
    right = [
        (0, "**无仪器演示模式**：模块入口 NoInstrumentDemo 检查。纯产品模块（M15 充电）、预留不判定（M14 SpO2）不拦截。", False),
        (0, "**配置生效时机**：Load() 走文件时间戳缓存、Save 后即刷新；但模块库/明细表是快照，改完配置要重开模块库、加载方案或点运行才重建。", False),
        (0, "**产品通讯集中在 M16**：其他模块不再重复握手，读方案级 _productCommOk 记录。", False),
        (0, "**日志规范**：每方法 Start/Ended 三段式；临时诊断代码确认后要删，别污染日志。", False),
    ]
    bullets(s, 0.6, 1.6, 6.05, 4.7, left, size=14.5, gap=22)
    bullets(s, 6.85, 1.6, 5.9, 4.7, right, size=14.5, gap=22)
    rect(s, 0.6, 6.35, 12.15, 0.62, fill=LIGHT, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s, 0.82, 6.42, 11.7, 0.5,
         [[("三种运行模式都要能收发/单独控制仪器：① 正常（仪器+产品齐全） ② 无产品演示（只跑仪器，产品项 NotJudged） ③ 无仪器演示（只读产品/单独控制）", 12, True, NAVY)]],
         anchor=MSO_ANCHOR.MIDDLE)
    footer(s, 12)


def s13_summary():
    s = slide()
    rect(s, 0, 0, SW, SH, fill=NAVY)
    rect(s, 0, 0, 0.28, SH, fill=TEAL)
    text(s, 0.95, 0.7, 11.5, 0.9, [[("速查总结：从哪开始", 34, True, WHITE)]])
    rows = [
        ("想懂全局", "Slide 3（分层）+ Slide 5（执行流）"),
        ("想调 Bug", "Slide 7 对号入座下断点"),
        ("想加功能", "Slide 8/9/10 照步骤抄（加模块 / 加设备 / 动态规格）"),
        ("别乱改", "Slide 11 稳定核心清单"),
        ("守约定", "Slide 12 协作约定与坑"),
    ]
    y = 1.85
    for k, v in rows:
        rect(s, 0.95, y, 2.6, 0.72, fill=TEAL, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
        box_text(s.shapes[-1], [[(k, 16, True, WHITE)]])
        rect(s, 3.7, y, 8.65, 0.72, fill=RGBColor(0x2A, 0x3C, 0x5E), shape=MSO_SHAPE.ROUNDED_RECTANGLE)
        box_text(s.shapes[-1], [[(v, 14.5, False, RGBColor(0xDD, 0xE6, 0xF0))]], align=PP_ALIGN.LEFT)
        y += 0.86
    rect(s, 0.95, 6.35, 11.4, 0.06, fill=TEAL)
    text(s, 0.95, 6.5, 11.4, 0.7,
         [[("一句话：Spec 定义测什么 → TestItem 执行怎么测 → TestItemResult 装结果 → MainViewModel 调度 → UI 判定显示；靠「项名」+「DI 契约」解耦。", 14, True, RGBColor(0x8F, 0xD6, 0xDE))]],
         anchor=MSO_ANCHOR.MIDDLE)


# ---------------- 生成 ----------------
for fn in [s01_cover, s02_agenda, s03_layers, s04_roles, s05_flow, s06_contract,
           s07_debug, s08_ext_module, s09_ext_device, s10_dynamic_spec, s11_stable,
           s12_conventions, s13_summary]:
    fn()

OUT = "docs/TORNPISolution架构与开发指南.pptx"
prs.save(OUT)
print("生成成功：", OUT, " 共", len(prs.slides._sldIdLst), "页")
