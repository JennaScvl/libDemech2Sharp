using System;
using System.Collections.Generic;

namespace Demech2
{
    // The in-mission menus (demech2 MW2/src/menu.c: RegisterMenu, RequestMenu, ToggleMenu, LayoutMenu, ActivateMenu,
    // DeactivateMenu, RequestMenuClose, FirstMenu, UpdateMenuKey, UpdateMenus, DrawAndRunMenu, ApplyMenuKey,
    // RunMenuItems; menucontrols.c: RunMenuStatus, RunMenuChoice; commandmenu.c, commandpointmenu.c, lancemenu.c: the
    // command computer and the command points' pages and their actions). A menu opens on request (its game key), draws
    // its open page over the cockpit and acts on the frame's menu key: a digit picks an item (and accepts it), Escape
    // closes the page; a menu that takes navigation keys (flag 1, and the menu sinks) moves its selection too, and holds
    // the gameplay input. The command computer (menu 1) and command points 2 and 3 (menus 7 and 8) order the local
    // player's star (StarOrders.OrderStarSlot) and change its formation; the main menu (4: abort, brightness, audio, the
    // combat variables, flee; mainmenu.c, audiomenu.c, perf.c, dorcs.c's actions) and the systems status (5: light
    // amplification, image enhancement, the HUD, auto eject; settings.c) set SystemSettings, DisplayBrightness and
    // Audio. The programmers' page (3, dorcs.c) opens at the end of the "dorcs" cheat's sequence (Dorcs). The keys
    // pressed, the monitor's brightness and the mission's end are the game's (Host.Input, Host.Cockpit).
    public static class SimMenus
    {
        // MenuChoices.
        public sealed class Choices
        {
            public Func<Definition, Control, int, Page, string> Suffix;
            public string[] Texts;
            public int Count => Texts.Length;
        }

        // MenuControl.
        public sealed class Control
        {
            public int Flags;  // 1: read the value through Get every time
            public int Value;
            public Choices Data;
            public int[] Shapes;   // a slider's: left cap, bar, right cap and knob (every other entry, 1.1 SHP ids)
            public int Arg;
            public Action<Page, Control> Init;
            public Func<int, int> Get;
            public Action<int, int> Preview, Set;
            public Action<int> Cancel;

            // A text box's (MenuTextBox): its rectangle, 16.16 fractions of the menu's target until first drawn, and text.
            public Pane TextRect;
            public bool TextRectPlaced;
            public string Text;
        }

        public delegate void ItemRun(Definition p_menu, Control p_control, int p_index, int p_x, int p_y, Page p_page);

        // MenuItem: type 0 opens the subpage, 1 does nothing, 2 accepts (and goes back), 3 is a heading without a
        // number, 5 and 6 close the page.
        public sealed class Item
        {
            public int Type;
            public string Text;
            public ItemRun Run;
            public Control Control;
            public Page Subpage;

            public Item Copy() => (Item)MemberwiseClone();
        }

        // MenuPage: its state (0 new, 1 opening, 2 open, 3 opening a subpage, 4 accepted, 5 cancelled), its title, the
        // AI slot it commands, its items (some of which its init copies from the templates at the end) and the selection.
        public sealed class Page
        {
            public int State;
            public string Title;
            public int AiSlot;
            public int ItemCount;
            public int Selected;
            public Func<Definition, Page, bool> Init;
            public readonly Item[] Items = new Item[16];
        }

        // MenuDefinition, its panes as 16.16 fractions of the screen until laid out.
        public sealed class Definition
        {
            public Pane Target, BackgroundTarget;
            public int Flags;          // 1: takes navigation keys, 2: waits out a stretch, 4: outlined, 8: no gap before the back item, 0x20: wipes its target
            public readonly Page[] PageStack = new Page[8];
            public int PageDepth;
            public int BackgroundId = -1, CursorShapeId = -1; // SHP resources (1.1 ids, plus the art resolution)
            public int OpenSound = -1, MoveSound = -1;
            public int FontId;
            public int Color, HighlightColor;
            public int LineCount;
            public int TextOriginX, TextOriginY, TitleX, TitleY, CursorX, CursorY, ItemX, ItemY, ControlX, ControlY;
            public Page RootPage;
        }

        // MenuSlot.
        sealed class Slot
        {
            public int Id;
            public int State;
            public int Requested;
            public Definition Definition;
        }

        // g_menuDefinitions: 1 the command computer, 7 and 8 command points 2 and 3.
        static readonly Definition[] s_definitions = new Definition[11];
        static readonly List<Slot> s_slots = new List<Slot>();
        static int s_openMenuCount;

        // g_menuKey: the key the open menu acts on this frame (0 none): '0' to '9', 0x1b, and for a navigating menu 9,
        // 0x0d, 0x20, 0xc6 (up), 0xc7 (down), 0xc8 (right), 0xc9 (left), 0x209 (shift-tab).
        static int s_menuKey;

        // g_menuRepeatTimer: the menu sinks repeat while held (GetTicks: real time, as the sim clock stops while paused).
        static int s_repeatStart;

        static int RealTicks => Host.Cockpit.RealTicks;

        // Not in the original: an open menu pauses the sim (SimLoop).
        public static bool Paused => s_openMenuCount > 0;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => FreeMenus();

        // ---- The mission's menus ----------------------------------------------------------------------------------

        // FirstMenu, then RegisterMenu for menus 4, 5, 1, 7, 8 and 3: the definitions laid out on the screen and their
        // pages prepared (one whose pages don't all prepare is dropped).
        public static void FirstMenu()
        {
            FreeMenus();
            Array.Clear(s_definitions, 0, s_definitions.Length);
            Array.Copy(LanceMenu.startingOrders, SlotOrders, 8);
            BuildCommandMenus();
            BuildMainMenus();
            BuildDorcsMenu();
            for (int i = 0; i <= 10; i++)
            {
                Definition menu = s_definitions[i];
                if (menu == null) continue;
                bool ok = false;
                LayoutMenu(menu);
                Page page = menu.RootPage;
                if (page != null)
                {
                    ok = true;
                    if (page.Init != null) ok &= page.Init(menu, page);
                    for (int j = 0; j < page.ItemCount; j++)
                    {
                        Page subpage = page.Items[j]?.Subpage;
                        if (subpage?.Init != null) ok &= subpage.Init(menu, subpage);
                    }
                }

                if (!ok) s_definitions[i] = null;
            }

            foreach (int id in new[] { 4, 5, 1, 7, 8, 3 }) s_slots.Add(new Slot { Id = id });
        }

        // FreeMenus: every slot closed and dropped (the menus can't open again this mission).
        public static void FreeMenus()
        {
            foreach (Slot slot in s_slots)
            {
                if (slot.Definition != null) DeactivateMenu(slot);
            }

            s_slots.Clear();
            s_openMenuCount = 0;
        }

        static Slot FindMenuSlot(int p_id) => s_slots.Find(s => s.Id == p_id);

        // RequestMenu: menu p_id opens, if no menu is open already.
        public static bool RequestMenu(int p_id)
        {
            if (s_openMenuCount != 0) return false;
            Slot slot = FindMenuSlot(p_id);
            if (slot == null) return false;
            slot.Requested = 1;
            return true;
        }

        // ToggleMenu: a closed menu opens, an open one closes.
        public static bool ToggleMenu(int p_id)
        {
            switch (GetMenuSlotState(p_id))
            {
                case 1:
                    RequestMenuClose(p_id);
                    break;
                case 0:
                    RequestMenu(p_id);
                    return true;
            }

            return false;
        }

        // RequestMenuClose.
        public static void RequestMenuClose(int p_id)
        {
            Slot slot = FindMenuSlot(p_id);
            if (slot != null) slot.Requested = 0;
        }

        // GetMenuSlotState: 1 open, else 0.
        public static int GetMenuSlotState(int p_id) => FindMenuSlot(p_id)?.State ?? 0;

        // GetOpenMenu.
        static Definition GetOpenMenu() => s_slots.Find(s => s.State == 1)?.Definition;

        public static bool AnyMenuOpen => GetOpenMenu() != null;

        // CloseInGameMenus (dorcs.c): every menu but the dorcs page asked to close.
        public static void CloseInGameMenus()
        {
            foreach (int id in new[] { 6, 2, 4, 7, 8, 5 }) RequestMenuClose(id);
        }

        // LayoutMenu: the target on the screen, its m_lineCount lines spaced evenly down it, the points in pixels of it.
        static void LayoutMenu(Definition p_menu)
        {
            HudShape background = p_menu.BackgroundId != -1 ? Art.Shape11(p_menu.BackgroundId + Art.g_artResolution) : null;
            if (background != null)
            {
                int size = ShapeBounds(background, 0);
                int scaleX = Fixed.Div16(Fixed.Div16(size >> 16, ScreenScale.g_screenWidthMinus1 + 1),
                    p_menu.BackgroundTarget.X1 - p_menu.BackgroundTarget.X0 + 1);
                int scaleY = Fixed.Div16(Fixed.Div16(size & 0xffff, ScreenScale.g_screenHeightMinus1 + 1),
                    p_menu.BackgroundTarget.Y1 - p_menu.BackgroundTarget.Y0 + 1);
                p_menu.BackgroundTarget = ScaleRectAboutCenter(p_menu.BackgroundTarget, scaleX, scaleY);
            }

            int lineY = Fixed.Div16(1, p_menu.LineCount + 2);
            int titleY = Fixed.Div16(lineY, 0x20000);
            p_menu.Target = ScreenScale.ScaleRectToScreen(p_menu.Target);
            p_menu.BackgroundTarget = ScreenScale.ScaleRectToScreen(p_menu.BackgroundTarget);
            (p_menu.TextOriginX, p_menu.TextOriginY) = ScreenScale.ScalePointToFrame(p_menu.Target, 0, lineY);
            (p_menu.TitleX, p_menu.TitleY) = ScreenScale.ScalePointToFrame(p_menu.Target, p_menu.TitleX, titleY);
            (p_menu.CursorX, p_menu.CursorY) = ScreenScale.ScalePointToFrame(p_menu.Target, p_menu.CursorX, lineY + lineY);
            (p_menu.ItemX, p_menu.ItemY) = ScreenScale.ScalePointToFrame(p_menu.Target, p_menu.ItemX, lineY + lineY);
            (p_menu.ControlX, p_menu.ControlY) = ScreenScale.ScalePointToFrame(p_menu.Target, p_menu.ControlX, lineY + lineY);
            if ((p_menu.Flags & 0x10) != 0)
            {
                int dx = p_menu.BackgroundTarget.X0 - p_menu.Target.X0;
                p_menu.BackgroundTarget.X0 -= dx;
                p_menu.BackgroundTarget.X1 -= dx;
            }
        }

        // VFX_shape_bounds: the frame's first dword, its width in the high word and its height in the low.
        static int ShapeBounds(HudShape p_shape, int p_frame)
        {
            byte[] data = p_shape.Data;
            int entry = 8 + p_frame * 8;
            if (entry + 4 > data.Length) return 0;
            int frame = BitConverter.ToInt32(data, entry);
            return frame >= 0 && frame + 4 <= data.Length ? BitConverter.ToInt32(data, frame) : 0;
        }

        // ScaleRectAboutCenter.
        static Pane ScaleRectAboutCenter(Pane p_src, int p_scaleX, int p_scaleY)
        {
            int centerX = ((p_src.X1 - p_src.X0 + 1) >> 1) + p_src.X0, centerY = ((p_src.Y1 - p_src.Y0 + 1) >> 1) + p_src.Y0;
            return new Pane(Fixed.Mul16(p_src.X0 - centerX, p_scaleX) + centerX, Fixed.Mul16(p_src.Y0 - centerY, p_scaleY) + centerY,
                Fixed.Mul16(p_src.X1 - centerX, p_scaleX) + centerX, Fixed.Mul16(p_src.Y1 - centerY, p_scaleY) + centerY);
        }

        // ActivateMenu: the menu open on its root page; one that takes navigation keys holds the gameplay input.
        static bool ActivateMenu(Slot p_slot)
        {
            s_repeatStart = RealTicks;
            Definition menu = p_slot.Id >= 1 && p_slot.Id <= 10 ? s_definitions[p_slot.Id] : null;
            p_slot.Definition = menu;
            if (menu == null) return false;
            menu.PageDepth = 0;
            PushMenuPage(menu, menu.RootPage);
            s_openMenuCount++;
            if ((menu.Flags & 1) != 0) InputMap.GameplayInputEnabled = false;
            return true;
        }

        // DeactivateMenu.
        static void DeactivateMenu(Slot p_slot)
        {
            Definition menu = p_slot.Definition;
            if (menu != null && (menu.Flags & 1) != 0) InputMap.GameplayInputEnabled = true;
            p_slot.Definition = null;
            s_openMenuCount--;
        }

        // UpdateMenuKey: the frame's menu key for the open menu, from the keys pressed (digits and Escape; a navigating
        // menu's Tab, Enter, Space and arrows: the game's keyboard, Host.Input.MenuKeyPressed) or from the menu sinks of one
        // that navigates (repeating while held). A key the menu takes doesn't reach the game keys.
        public static void UpdateMenuKey()
        {
            s_menuKey = 0;
            if (!Host.Input.BeginMenuKeys()) return;
            Definition menu = GetOpenMenu();
            if (menu == null) return;
            int flags = menu.Flags;
            int keyCode = Host.Input.MenuKeyPressed((flags & 1) != 0);

            if (keyCode != 0)
            {
                s_menuKey = keyCode;
            }
            else if ((flags & 1) != 0)
            {
                if (InputMap.Get("menu.enter") != 0) s_menuKey = 0x0d;
                else if (InputMap.Get("menu.abort") != 0) s_menuKey = 0x1b;
                int ticks = RealTicks - s_repeatStart;
                if (s_menuKey == 0 && ticks > 90)
                {
                    int item = InputMap.Get("menu.item");
                    if (item < 0) s_menuKey = 0xc6;
                    else if (item > 0) s_menuKey = 0xc7;
                }

                if (s_menuKey == 0 && ticks > 45)
                {
                    int value = InputMap.Get("menu.value");
                    if (value < 0) s_menuKey = 0xc9;
                    else if (value > 0) s_menuKey = 0x20;
                }
            }

            if (s_menuKey != 0 && (flags & 1) != 0)
            {
                s_repeatStart = RealTicks;
                InputMap.Set("menu.item.center", 1);
                InputMap.Set("menu.value.center", 1);
            }
        }

        // UpdateMenus: menus opened and closed as requested, then the first open one drawn and run (DrawAndRunMenu; it
        // closes once its last page goes).
        public static void UpdateMenus(ICockpitSurface p_hud)
        {
            for (int i = 0; i < s_slots.Count; i++)
            {
                Slot slot = s_slots[i];
                int state = slot.State, requested = slot.Requested;
                if (state != requested)
                {
                    if (requested == 1)
                    {
                        if (ActivateMenu(slot)) state = 1;
                    }
                    else if (requested == 0)
                    {
                        DeactivateMenu(slot);
                        state = 0;
                    }
                }

                slot.State = state;
                slot.Requested = requested;
                if (state == 1)
                {
                    if (!DrawAndRunMenu(p_hud, slot.Definition)) slot.Requested = 0;
                    break;
                }
            }
        }

        // DrawAndRunMenu. FALSE once the menu should close.
        static bool DrawAndRunMenu(ICockpitSurface p_hud, Definition p_menu)
        {
            if (p_menu == null) return false;
            if ((p_menu.Flags & 0x20) != 0) p_hud.Wipe(p_menu.Target, 0);
            HudShape background = p_menu.BackgroundId != -1 ? Art.Shape11(p_menu.BackgroundId + Art.g_artResolution) : null;
            if (background != null) p_hud.Shape(p_menu.BackgroundTarget, background, 0, 0, 0);
            if ((p_menu.Flags & 4) != 0) Outline(p_hud, p_menu.Target, 1);
            RunMenuItems(p_hud, p_menu);
            return p_menu.PageDepth != 0;
        }

        static void Outline(ICockpitSurface p_hud, Pane p_target, int p_color)
        {
            int width = p_target.X1 - p_target.X0, height = p_target.Y1 - p_target.Y0;
            p_hud.Line(p_target, 0, 0, width, 0, p_color);
            p_hud.Line(p_target, width, 0, width, height, p_color);
            p_hud.Line(p_target, width, height, 0, height, p_color);
            p_hud.Line(p_target, 0, height, 0, 0, p_color);
        }

        // ApplyMenuKey: Enter by the item's type (0 opens its subpage: action 3; 2 accepts: 4; 5 and 6 close: 5), Escape
        // closes (5); Down and Tab move the selection down, Up and Shift-Tab up.
        static void ApplyMenuKey(int p_key, int p_itemType, ref int p_action, ref int p_move)
        {
            switch (p_key)
            {
                case 0x09:
                case 0xc7:
                    p_move = 1;
                    break;
                case 0xc6:
                case 0x209:
                    p_move = -1;
                    break;
                case 0x0d:
                    switch (p_itemType)
                    {
                        case 0:
                            p_action = 3;
                            break;
                        case 2:
                            p_action = 4;
                            break;
                        case 5:
                        case 6:
                            p_action = 5;
                            break;
                    }

                    break;
                case 0x1b:
                    p_action = 5;
                    break;
            }
        }

        // RunMenuItems: the open page's selection moved by the menu key (a digit picks an item, past the heading one on,
        // and acts as Enter; 0 the back item; the rest through ApplyMenuKey, which steps over headings), then its title
        // (underlined) and items drawn, numbered from 1 with the back item 0, each item's m_run seeing the menu key too;
        // finally the selected item's subpage opened (state 3) or the page closed (4 and 5).
        static void RunMenuItems(ICockpitSurface p_hud, Definition p_menu)
        {
            int heading = 0x10, move = 0, key = 0;
            bool hasHeading = false;
            VfxFont font = Art.FontOf(p_menu.FontId + Art.g_artResolution);
            if (font == null) return;
            Page page = PeekMenuPage(p_menu);
            if (page == null) return;
            Pane target = p_menu.Target;
            int selected = page.Selected;
            int back = -1;
            for (int i = 0; i < page.ItemCount; i++)
            {
                if (page.Items[i].Type == 2 || page.Items[i].Type == 6) back = i;
                if (page.Items[i].Type == 3)
                {
                    heading = i;
                    hasHeading = true;
                }
            }

            if (back == -1) hasHeading = false;

            if (page.State == 0) page.State = 1;
            else
            {
                if (page.State == 1) page.State = 2;
                if (s_menuKey != 0)
                {
                    key = s_menuKey;
                    if (s_menuKey >= '0' && s_menuKey <= '9')
                    {
                        int n = s_menuKey - '0';
                        if (n == 0)
                        {
                            if (back != -1)
                            {
                                selected = back;
                                key = '\r';
                            }
                        }
                        else if (n >= 1 && page.ItemCount - (hasHeading ? 1 : 0) > n)
                        {
                            if (n <= heading) selected = n - 1;
                            else
                            {
                                selected = n;
                                s_menuKey++;
                                if (s_menuKey > '9') s_menuKey = '0';
                            }

                            key = '\r';
                        }
                    }

                    ApplyMenuKey(key, page.Items[selected].Type, ref page.State, ref move);
                    if (move != 0) selected = (page.ItemCount + selected + move) % page.ItemCount;
                    if (page.Selected != selected && p_menu.MoveSound != -1) EffectSound.PlayMenuSound(Host.Anim.MapSoundId(p_menu.MoveSound));
                }
            }

            if (move == 0) move = 1;
            if (page.Items[selected].Type == 3)
            {
                selected = (page.ItemCount + selected + move) % page.ItemCount;
                if (s_menuKey >= '0' && s_menuKey <= '9')
                {
                    s_menuKey++;
                    if (s_menuKey > '9') s_menuKey = '0';
                    ApplyMenuKey(key, page.Items[selected].Type, ref page.State, ref move);
                    if (move == 0) move = 1;
                }
            }

            page.Selected = selected;
            int[] colors = Art.g_textColors;
            colors[0xe] = p_menu.Color;
            int textX = p_menu.TitleX, textY = p_menu.TitleY;
            if (page.Title != null)
            {
                p_hud.String(target, textX, textY, font, page.Title, colors);
                if ((p_menu.Flags & 4) != 0) p_hud.Line(target, 0, textY + font.Height, target.X1 - target.X0, textY + font.Height, 1);
                else
                {
                    int width = 0;
                    foreach (char c in page.Title) width += font.CharacterWidth(c);
                    p_hud.Line(target, textX, textY + font.Height, textX + width - 1, textY + font.Height, 1);
                }
            }

            int itemX = p_menu.ItemX, itemY = p_menu.ItemY, controlX = p_menu.ControlX, controlY = p_menu.ControlY;
            int cursorY = p_menu.CursorY + font.Height / 2;
            int numberWidth = font.CharacterWidth('0') * 2 + font.CharacterWidth('.');
            int number = 0, offset = 0;
            for (int i = 0; i < page.ItemCount; i++)
            {
                colors[0xe] = selected == i ? p_menu.HighlightColor : p_menu.Color;
                Item item = page.Items[i];
                if ((p_menu.Flags & 8) == 0 && back == i) offset += (p_menu.LineCount - i - 1) * p_menu.TextOriginY;
                itemY += offset;
                controlY += offset;
                cursorY += offset;
                offset = p_menu.TextOriginY;
                HudShape cursor = p_menu.CursorShapeId != -1 ? Art.Shape11(p_menu.CursorShapeId + Art.g_artResolution) : null;
                if (selected == i && cursor != null) p_hud.Shape(target, cursor, 0, p_menu.CursorX, cursorY);
                if (item.Type != 3)
                {
                    number = back == i ? 0 : number + 1;
                    p_hud.String(target, itemX, itemY, font, number.ToString(), colors);
                }

                if (item.Text != null) p_hud.String(target, itemX + numberWidth, itemY, font, item.Text, colors);
                item.Run?.Invoke(p_menu, item.Control, i, controlX, controlY, page);
            }

            colors[0xe] = 0xe;
            switch (page.State)
            {
                case 4:
                case 5:
                    page.State = 0;
                    page.Selected = 0;
                    PopMenuPage(p_menu);
                    break;
                case 3:
                    page.State = 2;
                    Page subpage = page.Items[selected].Subpage;
                    if (subpage != null)
                    {
                        PushMenuPage(p_menu, subpage);
                        if (p_menu.OpenSound != -1) EffectSound.PlayMenuSound(Host.Anim.MapSoundId(p_menu.OpenSound));
                    }

                    break;
            }
        }

        static Page PeekMenuPage(Definition p_menu) => p_menu.PageDepth > 0 ? p_menu.PageStack[p_menu.PageDepth - 1] : null;

        static void PushMenuPage(Definition p_menu, Page p_page)
        {
            if (p_menu.PageDepth < 8) p_menu.PageStack[p_menu.PageDepth++] = p_page;
        }

        static void PopMenuPage(Definition p_menu)
        {
            if (p_menu.PageDepth > 0) p_menu.PageDepth--;
        }

        // ---- Controls (menucontrols.c) ----------------------------------------------------------------------------

        // The control's value for the page's state: read when the page opens (its init first), set when it's accepted,
        // cancelled when it's cancelled. Returns whether it may preview.
        static bool RunControlState(Control p_control, Page p_page, ref int p_value)
        {
            switch (p_page.State)
            {
                case 1:
                    p_control.Init?.Invoke(p_page, p_control);
                    p_control.Value = p_control.Get != null ? p_control.Get(p_control.Arg) : 0;
                    p_value = p_control.Value;
                    break;
                case 4:
                    p_control.Set?.Invoke(p_control.Arg, p_value);
                    return false;
                case 5:
                    p_control.Cancel?.Invoke(p_control.Arg);
                    return false;
            }

            return true;
        }

        // RunMenuStatus: a list of choices shown with its suffix, changed only through its callbacks; its number key on a
        // closing item (5) sets it.
        static void RunMenuStatus(Definition p_menu, Control p_control, int p_index, int p_x, int p_y, Page p_page)
        {
            if (p_control?.Data == null) return;
            VfxFont font = Art.FontOf(p_menu.FontId + Art.g_artResolution);
            if (font == null) return;
            bool pressed = s_menuKey - '1' == p_index;
            int value = (p_control.Flags & 1) != 0 && p_control.Get != null ? p_control.Get(p_control.Arg) : p_control.Value;
            bool selected = p_page.Selected == p_index;
            bool apply = RunControlState(p_control, p_page, ref value);
            Choices choices = p_control.Data;
            if (choices.Count > 0)
            {
                value = ((choices.Count + value) % choices.Count + choices.Count) % choices.Count;
                string text = choices.Texts[value];
                string suffix = choices.Suffix?.Invoke(p_menu, p_control, p_index, p_page);
                if (suffix != null) text += suffix;
                if (text.Length > 127) text = text.Substring(0, 127);
                CurrentHud.String(p_menu.Target, p_x, p_y, font, text, Art.g_textColors);
            }

            if (selected)
            {
                if (pressed && p_page.Items[p_index].Type == 5) p_control.Set?.Invoke(p_control.Arg, value);
                if (p_control.Preview != null && apply) p_control.Preview(p_control.Arg, value);
            }

            p_control.Value = value;
        }

        // The HUD page the open menu draws on this frame.
        static ICockpitSurface CurrentHud;

        // UpdateMenus with the cockpit's HUD page.
        public static void Draw(ICockpitSurface p_hud)
        {
            CurrentHud = p_hud;
            UpdateMenus(p_hud);
            CurrentHud = null;
        }

        // RunMenuSlider: a slider from 0 to 0x10000 (its shapes: left cap, bar, right cap, knob): while selected, Space or
        // Right raises it a tenth and Left lowers it, its number key raises it too; the knob along the bar. A change sets
        // it at once on an item of type 4 (or its number key on type 5), else it previews until the page is accepted.
        static void RunMenuSlider(Definition p_menu, Control p_control, int p_index, int p_x, int p_y, Page p_page)
        {
            if (p_control?.Shapes == null) return;
            VfxFont font = Art.FontOf(p_menu.FontId + Art.g_artResolution);
            if (font == null) return;
            int step = 0;
            bool pressed = s_menuKey - '1' == p_index;
            int value = (p_control.Flags & 1) != 0 && p_control.Get != null ? p_control.Get(p_control.Arg) : p_control.Value;
            bool selected = p_page.Selected == p_index;
            if (p_page.State == 2 && selected)
            {
                switch (s_menuKey)
                {
                    case ' ':
                    case 0xc8:
                        step = 0x199a;
                        break;
                    case 0xc9:
                        step = -0x199a;
                        break;
                    default:
                        if (pressed) step = 0x199a;
                        break;
                }
            }

            bool apply = RunControlState(p_control, p_page, ref value);
            HudShape left = Art.Shape11(p_control.Shapes[0] + Art.g_artResolution);
            HudShape bar = Art.Shape11(p_control.Shapes[2] + Art.g_artResolution);
            HudShape knob = Art.Shape11(p_control.Shapes[6] + Art.g_artResolution);
            HudShape right = Art.Shape11(p_control.Shapes[4] + Art.g_artResolution);
            if (bar != null && knob != null)
            {
                int width = ShapeBounds(bar, 0) >> 16;
                value += step;
                if (value < 0) value = 0;
                if (value > 0x10000) value = 0x10000;
                int knobX = Fixed.Mul16(value, width);
                int y = p_y + font.Height / 2;
                int x = p_x;
                if (left != null)
                {
                    CurrentHud.Shape(p_menu.Target, left, 0, x, y);
                    x += ShapeBounds(left, 0) >> 16;
                }

                CurrentHud.Shape(p_menu.Target, bar, 0, x, y);
                if (right != null) CurrentHud.Shape(p_menu.Target, right, 0, x + width, y);
                x += knobX;
                x -= (ShapeBounds(knob, 0) >> 16) / 2;
                CurrentHud.Shape(p_menu.Target, knob, 0, x, y);
                if (selected)
                {
                    if (((step != 0 && p_page.Items[p_index].Type == 4) || (pressed && p_page.Items[p_index].Type == 5)) && p_control.Set != null)
                    {
                        p_control.Set(p_control.Arg, value);
                    }

                    if (p_control.Preview != null && apply) p_control.Preview(p_control.Arg, value);
                }
            }

            p_control.Value = value;
        }

        // RunMenuChoice: a list of choices: while selected, Space or Right the next, Left the previous (wrapping), its
        // number key the next; set at once on an item of type 4 (or the number key on type 5).
        static void RunMenuChoice(Definition p_menu, Control p_control, int p_index, int p_x, int p_y, Page p_page)
        {
            if (p_control?.Data == null) return;
            VfxFont font = Art.FontOf(p_menu.FontId + Art.g_artResolution);
            if (font == null) return;
            int step = 0;
            bool pressed = s_menuKey - '1' == p_index;
            int value = (p_control.Flags & 1) != 0 && p_control.Get != null ? p_control.Get(p_control.Arg) : p_control.Value;
            bool selected = p_page.Selected == p_index;
            if (p_page.State == 2 && selected)
            {
                switch (s_menuKey)
                {
                    case ' ':
                    case 0xc8:
                        step = 1;
                        break;
                    case 0xc9:
                        step = -1;
                        break;
                    default:
                        if (pressed) step = 1;
                        break;
                }
            }

            bool apply = RunControlState(p_control, p_page, ref value);
            Choices choices = p_control.Data;
            if (choices.Count > 0)
            {
                value = ((choices.Count + step + value) % choices.Count + choices.Count) % choices.Count;
                CurrentHud.String(p_menu.Target, p_x, p_y, font, choices.Texts[value], Art.g_textColors);
            }

            if (selected)
            {
                if (((step != 0 && p_page.Items[p_index].Type == 4) || (pressed && p_page.Items[p_index].Type == 5)) && p_control.Set != null)
                {
                    p_control.Set(p_control.Arg, value);
                }

                if (p_control.Preview != null && apply) p_control.Preview(p_control.Arg, value);
            }

            p_control.Value = value;
        }

        // AbortMissionAction (dorcs.c): Enter on it, or its number key: the local player ejects (vulnerable, and as if
        // the atmosphere were breathable), quietly; the menus go.
        static void AbortMissionAction(Definition p_menu, Control p_control, int p_index, int p_x, int p_y, Page p_page)
        {
            if (p_page == null) return;
            bool digit = s_menuKey - '1' == p_index;
            bool selected = p_page.Selected == p_index;
            if ((selected && s_menuKey == '\r') || digit)
            {
                SimMain.g_difficulty.m_invulnerable = false;
                bool saved = Host.Cockpit.SwapHostileAtmosphere(false);
                // GameKeys.RunGameKey(0x3b)'s eject, called directly: it ignores the key while this menu pauses the sim.
                SimMain.LocalMech?.EjectPlayer(true);
                Host.Cockpit.SwapHostileAtmosphere(saved);
                FreeMenus();
            }
        }

        // FleeToWindowsAction: Enter or its number key: the player flees (g_fledToWindows: the mission ends and the game
        // quits), its mech ejected; the menus go.
        static void FleeToWindowsAction(Definition p_menu, Control p_control, int p_index, int p_x, int p_y, Page p_page)
        {
            if (p_page == null) return;
            bool digit = s_menuKey - '1' == p_index;
            bool selected = p_page.Selected == p_index;
            if ((selected && s_menuKey == '\r') || digit)
            {
                Host.Cockpit.FleeToWindows();
                SimMain.g_difficulty.m_invulnerable = false;
                SimMain.LocalMech?.EjectPlayer(false);
                FreeMenus();
            }
        }

        // ---- The main menu (mainmenu.c, audiomenu.c, brightnessmenu.c, perf.c) and the systems status (settings.c) ----

        static readonly string[] OffOn = { "Off", "On" };
        static readonly string[] LowHigh = { "Low", "High" };
        const string AcceptText = "Accept (Esc to cancel)";

        // g_sliderShapes (1.1's).
        static readonly int[] SliderShapes = { 67, 0, 64, 0, 70, 0, 82, 0 };

        static void BuildMainMenus()
        {
            Item It(int p_type, string p_text, ItemRun p_run = null, Control p_control = null, Page p_subpage = null) =>
                new Item { Type = p_type, Text = p_text, Run = p_run, Control = p_control, Subpage = p_subpage };
            var offOn = new Choices { Texts = OffOn };
            var lowHigh = new Choices { Texts = LowHigh };
            var confirm = new Control { Flags = 2 };

            var abort = new Page { Title = "ABORT MISSION", ItemCount = 2 };
            Fill(abort, It(3, "Confirmation requested"), It(1, AcceptText, AbortMissionAction, confirm));

            var brightness = new Page { Title = "MONITOR BRIGHTNESS", ItemCount = 2 };
            var brightnessControl = new Control
            {
                Flags = 2, Shapes = SliderShapes, Get = Host.Cockpit.GetBrightness, Preview = Host.Cockpit.PreviewBrightness,
                Set = Host.Cockpit.SetBrightness, Cancel = Host.Cockpit.RestoreBrightness,
            };
            Fill(brightness, It(1, "Monitor Brightness", RunMenuSlider, brightnessControl), It(2, AcceptText));

            Control Sound(int p_setting) => new Control
            {
                Flags = 2, Shapes = SliderShapes, Arg = p_setting, Get = Audio.GetSoundSetting,
                Preview = Audio.PreviewSoundSetting, Set = Audio.SetSoundSetting, Cancel = Audio.RestoreSoundSetting,
            };
            var audio = new Page { Title = "SET AUDIO VOLUME", ItemCount = 4 };
            Fill(audio, It(1, "Music", RunMenuSlider, Sound(2)), It(1, "Sound Effects", RunMenuSlider, Sound(0)),
                It(1, "Voice", RunMenuSlider, Sound(1)), It(2, AcceptText));

            // The perf settings (ApplyPerfSettings, the getters and setters): MW2SND.CFG's, kept but inert (one detail
            // level).
            Control Perf(Choices p_choices, Func<int> p_get, Action<int> p_set) =>
                new Control { Flags = 2, Data = p_choices, Get = a => p_get(), Set = (a, v) => p_set(v) };
            var combat = new Page { Title = "COMBAT VARIABLES", ItemCount = 6 };
            Fill(combat,
                It(1, "Object textmaps", RunMenuChoice, Perf(offOn, () => Audio.Saved.ObjectTextmaps, v => Audio.Saved.ObjectTextmaps = v)),
                It(1, "Terrain textmaps", RunMenuChoice, Perf(offOn, () => Audio.Saved.TerrainTextmaps, v => Audio.Saved.TerrainTextmaps = v)),
                It(1, "Display detail", RunMenuChoice, Perf(lowHigh, () => Audio.Saved.DisplayDetail, v => Audio.Saved.DisplayDetail = v)),
                It(1, "Object density", RunMenuChoice, Perf(lowHigh, () => Audio.Saved.ObjectDensity, v => Audio.Saved.ObjectDensity = v)),
                It(1, "Explosion chunks", RunMenuChoice, Perf(offOn, () => Audio.Saved.ExplosionChunks, v => Audio.Saved.ExplosionChunks = v)),
                It(2, AcceptText));

            var flee = new Page { Title = "FLEE TO WINDOWS", ItemCount = 2 };
            Fill(flee, It(3, "Confirm your cowardice"), It(1, AcceptText, FleeToWindowsAction, confirm));

            var main = new Page { Title = "MAIN MENU", ItemCount = 6 };
            Fill(main, It(0, "Abort Mission", null, null, abort), It(0, "Monitor Brightness", null, null, brightness),
                It(0, "Audio Ctrl", null, null, audio), It(0, "Combat Variables", null, null, combat),
                It(0, "Flee to Windows", null, null, flee), It(2, AcceptText));

            s_definitions[4] = new Definition
            {
                Target = new Pane(0x4000, 0x3333, 0x10000, 0xcccd),
                BackgroundTarget = new Pane(0, 0, 0x10000, 0x10000),
                Flags = 17,
                BackgroundId = 0x112,
                OpenSound = 225,
                MoveSound = 219,
                FontId = 1,
                Color = 250,
                HighlightColor = 10,
                LineCount = 6,
                TitleX = 0xa3d,
                CursorX = 0xa3d,
                ItemX = 0xa3d,
                ControlX = 0x7852,
                RootPage = main,
            };

            Control Setting(int p_id) => new Control { Flags = 3, Data = offOn, Arg = p_id, Get = SystemSettings.GetSystemSetting, Set = SystemSettings.SetSystemSetting };
            var systems = new Page { Title = "SYSTEMS STATUS", ItemCount = 5 };
            Fill(systems, It(4, "Light Amplification", RunMenuChoice, Setting(0xa6)),
                It(4, "Image Emhancement", RunMenuChoice, Setting(0xa7)), It(4, "HUD", RunMenuChoice, Setting(0x13)),
                It(4, "Auto Eject", RunMenuChoice, Setting(0x3c)), It(6, EscToExit));
            s_definitions[5] = new Definition
            {
                Target = new Pane(0x8ccd, 0x547b, 0x10000, 0x9eb8),
                BackgroundTarget = new Pane(0x8ccd, 0x547b, 0x10000, 0x9eb8),
                Flags = 2,
                OpenSound = 225,
                MoveSound = 219,
                FontId = 1,
                Color = 14,
                HighlightColor = 14,
                LineCount = 6,
                CursorX = 0x51f,
                ItemX = 0x51f,
                ControlX = 0xcccd,
                RootPage = systems,
            };
        }

        // ---- The programmers' page (dorcs.c) -------------------------------------------------------------------------

        // The "dorcs" cheat's page (menu 3): the programmers, each a page of text
        // (StreamingAssets/Tables/CreditPages.json).
        static CreditPage[] CreditPages => SimTables.CreditPages;

        static void BuildDorcsMenu()
        {
            const string exit = "This is boring!";
            var root = new Page { Title = "MW2 Programmer Dorcs Page", ItemCount = CreditPages.Length + 1 };
            for (int i = 0; i < CreditPages.Length; i++)
            {
                var box = new Control { TextRect = new Pane(0, 0x2666, 0x10000, 0x10000), Text = CreditPages[i].body };
                var page = new Page { Title = CreditPages[i].heading, ItemCount = 2 };
                page.Items[0] = new Item { Type = 3, Run = RunMenuTextBox, Control = box };
                page.Items[1] = new Item { Type = 2, Text = exit };
                root.Items[i] = new Item { Type = 0, Text = CreditPages[i].heading, Subpage = page };
            }

            root.Items[CreditPages.Length] = new Item { Type = 2, Text = exit };
            s_definitions[3] = new Definition
            {
                Target = new Pane(0x199a, 0x199a, 0xe666, 0xe666),
                BackgroundTarget = new Pane(0x199a, 0x199a, 0xe666, 0xe666),
                Flags = 37,
                OpenSound = 225,
                MoveSound = 219,
                FontId = 1,
                Color = 16,
                HighlightColor = 1,
                LineCount = 15,
                CursorX = 0x51f,
                ItemX = 0x51f,
                ControlX = 0xc000,
                RootPage = root,
            };
        }

        // RunMenuTextBox (menucontrols.c): the text box's text wrapped in the menu's font; its rectangle placed in the
        // menu's target the first time.
        static void RunMenuTextBox(Definition p_menu, Control p_control, int p_index, int p_x, int p_y, Page p_page)
        {
            if (p_menu == null || p_page == null || p_control?.Text == null) return;
            VfxFont font = Art.FontOf(p_menu.FontId + Art.g_artResolution);
            if (font == null) return;
            if (!p_control.TextRectPlaced)
            {
                p_control.TextRect = ScreenScale.ScaleRectToFrame(p_menu.Target, p_control.TextRect);
                p_control.TextRectPlaced = true;
            }

            Cockpit.DrawWrappedText(CurrentHud, p_control.TextRect, p_control.Text, font);
            if (p_control.Preview != null && p_page.State != 4 && p_page.State != 5) p_control.Preview(p_control.Arg, 0);
        }

        // ---- The command menus (commandmenu.c, commandpointmenu.c, lancemenu.c) ------------------------------------

        // The command menus' texts and the orders the slots start with (StreamingAssets/Tables/LanceMenuTexts.json): the
        // formations, the orders (by a slot's order) and the AI states (by state plus one, each followed by the player's
        // goal).
        static int[] s_slotOrderState;

        static LanceMenuFile LanceMenu => SimTables.LanceMenuTexts;

        // The order each AI slot was given last (an index into the orders' choices, 7 none); slot 0 is the star's.
        static int[] SlotOrders => s_slotOrderState ??= (int[])LanceMenu.startingOrders.Clone();

        static string[] FormationTexts => LanceMenu.formations;
        static string[] OrderTexts => LanceMenu.orders;
        static string[] AiStateTexts => LanceMenu.slotStates;

        const string EscToExit = "(Esc to exit)";

        // The definitions of menus 1, 7 and 8, their pages and controls, as the original's data has them (the controls
        // that order a slot are shared by every command point page, each setting its slot as it opens).
        static void BuildCommandMenus()
        {
            var formationChoices = new Choices { Texts = FormationTexts };
            var orderChoices = new Choices { Texts = OrderTexts };
            var aiStateChoices = new Choices { Texts = AiStateTexts, Suffix = GetSlotGoalName };
            var noChoices = new Choices { Texts = new string[0] };

            var formationControl = new Control { Flags = 3, Data = formationChoices, Get = p_arg => Teams.GetTeamFormation(SimMain.g_world, SimMain.g_world.m_localStar) };
            var commandAllStatus = new Control { Flags = 3, Data = orderChoices, Get = GetLanceOrder };
            Control SlotStatus(int p_slot) => new Control { Flags = 3, Data = aiStateChoices, Arg = p_slot, Init = InstallGoalSuffix, Get = GetSlotAiState };
            Control statusPoint2 = SlotStatus(1), statusPoint3 = SlotStatus(2), statusPoint4 = SlotStatus(3), statusPoint5 = SlotStatus(4);
            Control Formation(int p_index) => new Control { Flags = 2, Data = noChoices, Arg = p_index, Set = SelectFormation };
            Control Order(Action<int, int> p_set) => new Control { Flags = 2, Data = noChoices, Init = SetControlSlot, Set = p_set };
            Control attack = Order(OrderAttack), engage = Order(OrderEngageAtWill), join = Order(OrderJoinFormation);
            Control defend = Order(OrderDefend), disengage = Order(OrderDisengage), shutdown = Order(OrderShutdown);

            Item It(int p_type, string p_text, ItemRun p_run = null, Control p_control = null, Page p_subpage = null) =>
                new Item { Type = p_type, Text = p_text, Run = p_run, Control = p_control, Subpage = p_subpage };

            var changeFormation = new Page { Title = "CHANGE FORMATION", ItemCount = 8 };
            Fill(changeFormation, It(3, "Current Form:", RunMenuStatus, formationControl),
                It(5, FormationTexts[0], RunMenuStatus, Formation(0)), It(5, FormationTexts[1], RunMenuStatus, Formation(1)),
                It(5, FormationTexts[2], RunMenuStatus, Formation(2)), It(5, FormationTexts[3], RunMenuStatus, Formation(3)),
                It(5, FormationTexts[4], RunMenuStatus, Formation(4)), It(5, FormationTexts[5], RunMenuStatus, Formation(5)),
                It(6, EscToExit));

            Page OrdersPage(string p_title, int p_slot, Control p_status, bool p_point)
            {
                var page = new Page { Title = p_title, AiSlot = p_slot, ItemCount = 7, Init = p_point ? PrepareCommandPointPage : null };
                Fill(page, It(3, "Status:", RunMenuStatus, p_status),
                    It(5, "Attack My Target", RunMenuStatus, attack), It(5, "Defend My Target", RunMenuStatus, defend),
                    It(5, "Join Formation", RunMenuStatus, join), It(5, "Engage at Will", RunMenuStatus, engage),
                    It(5, "Shutdown", RunMenuStatus, shutdown), It(6, EscToExit), p_point ? It(3, "NOT AVAILABLE") : null);
                return page;
            }

            Page commandAll = OrdersPage("COMMAND ALL", 0, commandAllStatus, false);
            Page point2 = OrdersPage("COMMAND POINT 2", 1, statusPoint2, true);
            Page point3 = OrdersPage("COMMAND POINT 3", 2, statusPoint3, true);
            Page point4 = OrdersPage("COMMAND POINT 4", 3, statusPoint4, true);
            Page point5 = OrdersPage("COMMAND POINT 5", 4, statusPoint5, true);

            var computer = new Page { Title = "COMMAND COMPUTER", ItemCount = 8, Init = PrepareCommandComputerPage };
            Fill(computer, It(0, "Change Formation", RunMenuStatus, formationControl, changeFormation),
                It(0, "Command Point 2", RunMenuStatus, statusPoint2, point2), It(0, "Command Point 3", RunMenuStatus, statusPoint3, point3),
                It(0, "Command Point 4", RunMenuStatus, statusPoint4, point4), It(0, "Command Point 5", RunMenuStatus, statusPoint5, point5),
                It(0, "Command All", RunMenuStatus, commandAllStatus, commandAll), It(6, EscToExit), It(3, "NO STAR MATES"));

            Definition Menu(Page p_root) => new Definition
            {
                Target = new Pane(0x3852, 0x4ccd, 0x10000, 0x999a),
                BackgroundTarget = new Pane(0x3852, 0x4ccd, 0x10000, 0x999a),
                Flags = 10,
                OpenSound = 225,
                MoveSound = 219,
                FontId = 1,
                Color = 14,
                HighlightColor = 14,
                LineCount = 8,
                CursorX = 0x51f,
                ItemX = 0x51f,
                ControlX = 0x6666,
                RootPage = p_root,
            };

            s_definitions[1] = Menu(computer);
            s_definitions[7] = Menu(point2);
            s_definitions[8] = Menu(point3);
        }

        static void Fill(Page p_page, params Item[] p_items)
        {
            for (int i = 0; i < p_items.Length && i < 16; i++) p_page.Items[i] = p_items[i];
        }

        // PrepareCommandComputerPage: an item per star mate (template 5 after them, and the closing one), the destroyed
        // ones' marked (type 1); without any (or with every one destroyed) only "NO STAR MATES" and the closing item.
        static bool PrepareCommandComputerPage(Definition p_menu, Page p_page)
        {
            if (p_page == null) return false;
            int marked = 0;
            int count = StarOrders.GetLocalStarSize() - 1;
            if (count != 0)
            {
                int slot = count + 1;
                p_page.Items[slot] = p_page.Items[5].Copy();
                slot++;
                p_page.Items[slot] = p_page.Items[6].Copy();
                p_page.ItemCount = slot + 1;
                for (int i = 0; i < count; i++)
                {
                    int index = StarOrders.FindStarSlotPlayer(i + 1);
                    Mech mech = SimMain.MechOf(index);
                    if (index >= 0 && index < SimMain.PlayerCount && mech != null && mech.m_aiState == 12)
                    {
                        p_page.Items[i + 1].Type = 1;
                        marked++;
                    }
                }
            }

            if (count == 0 || marked == count)
            {
                p_page.Items[0] = p_page.Items[7].Copy();
                p_page.Items[1] = p_page.Items[6].Copy();
                p_page.ItemCount = 2;
            }

            return true;
        }

        // PrepareCommandPointPage: unless the page's slot holds a star mate not destroyed, only its last two items (NOT
        // AVAILABLE and the closing one).
        static bool PrepareCommandPointPage(Definition p_menu, Page p_page)
        {
            if (p_page == null) return false;
            int count = StarOrders.GetLocalStarSize() - 1;
            bool valid = p_page.AiSlot <= count;
            if (valid)
            {
                int index = StarOrders.FindStarSlotPlayer(p_page.AiSlot);
                Mech mech = SimMain.MechOf(index);
                // FirstMenu runs before the mechs' AI is set up: a player without one is in state 0.
                valid = index >= 0 && index < SimMain.PlayerCount && (mech == null || mech.m_aiState != 12);
            }

            if (!valid)
            {
                p_page.Items[0] = p_page.Items[p_page.ItemCount].Copy();
                p_page.Items[1] = p_page.Items[p_page.ItemCount - 1].Copy();
                p_page.ItemCount = 2;
            }

            return true;
        }

        static int GetLanceOrder(int p_index) => p_index < 8 ? SlotOrders[p_index] : 7;

        // SelectFormation: the star's formation (SetTeamFormation, named to the star).
        static void SelectFormation(int p_formation, int p_value)
        {
            SlotOrders[0] = 0;
            Teams.SetTeamFormation(SimMain.g_world, SimMain.g_world.m_localStar, p_formation);
            RequestMenuClose(1);
        }

        // GetSlotAiState: the AI state of the star mate in slot p_index, plus one (0: none).
        static int GetSlotAiState(int p_index)
        {
            int state = -1;
            if (p_index < 8)
            {
                int index = StarOrders.FindStarSlotPlayer(p_index);
                Mech mech = index != -1 ? SimMain.MechOf(index) : null;
                if (mech != null) state = mech.m_aiState;
            }

            return state + 1;
        }

        static void SetControlSlot(Page p_page, Control p_control)
        {
            if (p_page != null && p_control != null) p_control.Arg = p_page.AiSlot;
        }

        static void InstallGoalSuffix(Page p_page, Control p_control)
        {
            if (p_page != null && p_control?.Data != null) p_control.Data.Suffix = GetSlotGoalName;
        }

        // The orders' actions: each records the order and gives it to the slot (OrderStarSlot).
        static void OrderAttack(int p_index, int p_value) => Order(p_index, 2, Mech.c_aiStateTarget);
        static void OrderEngageAtWill(int p_index, int p_value) => Order(p_index, 1, Mech.c_aiStateAttack);
        static void OrderJoinFormation(int p_index, int p_value) => Order(p_index, 3, Mech.c_aiStateFollow);
        static void OrderDefend(int p_index, int p_value) => Order(p_index, 4, Mech.c_aiStatePatrol);
        static void OrderDisengage(int p_index, int p_value) => Order(p_index, 5, Mech.c_aiStateGoDirect);
        static void OrderShutdown(int p_index, int p_value) => Order(p_index, 6, Mech.c_aiStateShutdown);

        static void Order(int p_index, int p_order, int p_command)
        {
            if (p_index < 8)
            {
                SlotOrders[p_index] = p_order;
                Host.Cockpit.RunStarCommand(p_command, p_index);
            }

            RequestMenuClose(1);
        }

        // GetSlotGoalName: the name of the slot's star mate's goal (a nav, a game thing or a player); none in formation.
        static string GetSlotGoalName(Definition p_menu, Control p_control, int p_index, Page p_page)
        {
            int index = StarOrders.FindStarSlotPlayer(p_control.Arg);
            if (index < 0 || index >= SimMain.PlayerCount) return null;
            Mech mech = SimMain.MechOf(index);
            if (mech == null || mech.m_aiState == 5) return null;
            int goal = mech.m_aiGoal, target = goal & 0xff;
            World world = SimMain.g_world;
            switch (goal & 0xf00)
            {
                case 0x100:
                    return target < world.m_navs.Count ? world.m_navs[target].m_name : null;
                case 0x400:
                    return target < world.m_gameThings.Count ? world.m_gameThings[target].m_name : null;
                case 0x200:
                    return target < world.m_players.Count ? world.m_players[target].m_name : null;
            }

            return null;
        }
    }
}
