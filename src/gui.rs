//! High-quality immediate-mode UI; backend is native Rust (egui/eframe).
//! No browser engine, .NET runtime, injected hooks or synthetic PiP sources.
use crate::{
    engine::{self, Engine, PipWindow, Settings, WindowKind},
    service::{self, Event},
};
use eframe::egui::{
    self, Align, Align2, Button, CentralPanel, Color32, ComboBox, FontId, Frame, Layout, Margin,
    Panel, RichText, Sense, Slider, Stroke, StrokeKind, Vec2, ViewportBuilder, ViewportCommand,
};
use serde_json::{Value, json};
use std::{
    io,
    sync::mpsc::{self, Receiver},
    time::{Duration, Instant},
};
use windows_sys::Win32::Foundation::HWND;

const BG: Color32 = Color32::from_rgb(10, 17, 29);
const SIDEBAR: Color32 = Color32::from_rgb(13, 23, 38);
const PANEL: Color32 = Color32::from_rgb(18, 30, 47);
const PANEL_LIGHT: Color32 = Color32::from_rgb(24, 40, 60);
const BORDER: Color32 = Color32::from_rgb(39, 59, 78);
const MUTED: Color32 = Color32::from_rgb(142, 162, 179);
const WHITE: Color32 = Color32::from_rgb(240, 247, 253);
const CYAN: Color32 = Color32::from_rgb(58, 222, 226);
const CYAN_DARK: Color32 = Color32::from_rgb(17, 87, 96);
const GREEN: Color32 = Color32::from_rgb(78, 227, 163);

fn secondary(s: impl Into<String>, size: f32) -> RichText {
    RichText::new(s.into()).color(MUTED).size(size)
}
fn heading(s: impl Into<String>, size: f32) -> RichText {
    RichText::new(s.into()).color(WHITE).size(size).strong()
}
fn tile() -> Frame {
    Frame::new()
        .fill(PANEL)
        .stroke(Stroke::new(1., BORDER))
        .corner_radius(18)
        .inner_margin(Margin::symmetric(21, 19))
}
fn action_button(title: &str, primary: bool) -> Button<'_> {
    Button::new(
        RichText::new(title)
            .size(14.)
            .strong()
            .color(if primary { BG } else { WHITE }),
    )
    .fill(if primary { CYAN } else { PANEL_LIGHT })
    .stroke(if primary {
        Stroke::NONE
    } else {
        Stroke::new(1., BORDER)
    })
    .corner_radius(10)
    .min_size(Vec2::new(118., 40.))
}
fn small_button(title: &str) -> Button<'_> {
    Button::new(RichText::new(title).color(WHITE).size(13.))
        .fill(PANEL_LIGHT)
        .stroke(Stroke::new(1., BORDER))
        .corner_radius(9)
        .min_size(Vec2::new(72., 36.))
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum Page {
    Overview,
    Windows,
    Settings,
}

pub struct Dashboard {
    engine: Engine,
    settings: Settings,
    rx: Receiver<Event>,
    candidates: Vec<PipWindow>,
    selected: Option<i64>,
    last_scan: Instant,
    activity: String,
    page: Page,
    manual_confirm: Option<PipWindow>,
    exiting: bool,
}
impl Dashboard {
    pub fn new(rx: Receiver<Event>) -> io::Result<Self> {
        let settings = Settings::load();
        let engine = Engine::new(&settings)?;
        let mut app = Self {
            engine,
            settings,
            rx,
            candidates: Vec::new(),
            selected: None,
            last_scan: Instant::now() - Duration::from_secs(3),
            activity: "Monitoring browser PiP and Discord stream pop-outs.".to_owned(),
            page: Page::Overview,
            manual_confirm: None,
            exiting: false,
        };
        app.scan(true);
        Ok(app)
    }
    fn save(&mut self) {
        if let Err(e) = self.settings.save() {
            self.log(format!("Settings: {e}"));
        }
    }
    fn log(&mut self, message: impl Into<String>) {
        self.activity = message.into();
    }
    fn scan(&mut self, force: bool) {
        if !force && self.last_scan.elapsed() < Duration::from_millis(900) {
            return;
        }
        self.last_scan = Instant::now();
        self.engine.enforce();
        let windows = engine::scan();
        if let Some(old) = self.selected {
            if !windows.iter().any(|w| w.handle == old) {
                self.selected = None;
            }
        }
        if self.selected.is_none() {
            self.selected = windows
                .iter()
                .find(|w| w.strict_match)
                .or_else(|| windows.first())
                .map(|w| w.handle);
        }
        if self.settings.auto_detect && self.settings.auto_enable {
            let captured = self.engine.handles();
            let auto_discord = self.settings.auto_discord_streams;
            for w in windows.iter().filter(|w| {
                w.strict_match
                    && !captured.contains(&w.handle)
                    && (!matches!(
                        w.kind,
                        WindowKind::DiscordStream | WindowKind::DiscordPopout
                    ) || auto_discord)
            }) {
                if let Err(e) = self.engine.capture(w) {
                    self.log(format!("Auto-capture failed: {e}"));
                } else {
                    self.log(format!(
                        "Video connected: {} (0x{:X})",
                        engine::browser(&w.process_name),
                        w.handle
                    ));
                }
            }
        }
        self.candidates = windows;
    }
    fn set_click(&mut self, value: bool) {
        self.settings.auto_enable = value;
        self.engine.set_click_through(value);
        self.save();
        self.log(if value {
            "Click-through enabled."
        } else {
            "Interactive mode enabled. Video controls are accessible."
        });
    }
    fn toggle(&mut self) {
        self.set_click(!self.engine.click_through);
    }
    fn opacity(&mut self, value: i32) {
        self.engine.set_opacity(value);
        self.settings.opacity = self.engine.opacity;
        self.save();
    }
    fn restore(&mut self) {
        // Disable auto-enable first so next scan cannot immediately capture again.
        self.set_click(false);
        match self.engine.restore_all() {
            Ok(()) => self.log("All captured windows restored to their original styles."),
            Err(e) => self.log(format!("Restore incomplete; journal retained: {e}")),
        }
    }
    fn capture_verified(&mut self, w: &PipWindow) -> Result<(), String> {
        self.set_click(true);
        self.engine.capture(w).map_err(|e| e.to_string())?;
        self.log(format!(
            "Captured {} | 0x{:X}",
            engine::browser(&w.process_name),
            w.handle
        ));
        Ok(())
    }
    fn capture_selected(&mut self) {
        let Some(w) = self
            .selected
            .and_then(|h| self.candidates.iter().find(|w| w.handle == h))
            .cloned()
        else {
            self.log("No PiP window to capture.");
            return;
        };
        if w.minimized {
            self.log("Restore this Discord pop-out from the taskbar before capturing.");
            return;
        }
        if !w.strict_match {
            // Prevent accidental alteration of a full-screen browser/Discord window.
            if w.width > 1600 || w.height > 1000 {
                self.log("Refusing suspiciously large browser window. Try the actual PiP.");
                return;
            }
            self.manual_confirm = Some(w);
            return;
        }
        if let Err(e) = self.capture_verified(&w) {
            self.log(format!("Capture failed: {e}"));
        }
    }
    fn event(&mut self, ctx: &egui::Context, event: Event) {
        match event {
            Event::Show => {
                ctx.send_viewport_cmd(ViewportCommand::Visible(true));
                ctx.send_viewport_cmd(ViewportCommand::Focus);
            }
            Event::Exit => {
                self.exiting = true;
                ctx.send_viewport_cmd(ViewportCommand::Visible(true));
                ctx.send_viewport_cmd(ViewportCommand::Close);
            }
            Event::Hotkey(id) => match id {
                1 => self.event(ctx, Event::Show),
                2 => self.toggle(),
                3 => self.opacity((self.engine.opacity + 10).min(100)),
                4 => self.opacity((self.engine.opacity - 10).max(20)),
                5 => self.restore(),
                _ => {}
            },
            Event::Command(call) => {
                let reply = self.command(ctx, &call.command, &call.arg).to_string();
                let _ = call.respond.send(reply);
            }
        }
    }
    fn command(&mut self, ctx: &egui::Context, command: &str, arg: &str) -> Value {
        let cmd = command.trim().to_ascii_lowercase();
        match cmd.as_str() {
            "list" | "scan" => {
                let windows: Vec<_> = engine::scan()
                    .iter()
                    .map(|w| {
                        json!({"handle":format!("0x{:X}", w.handle), "pid":w.pid,
                        "browser":engine::browser(&w.process_name), "title":w.title,
                        "kind":w.kind,"confirmedStream":w.kind==WindowKind::DiscordStream && w.strict_match,
                        "discordPopout":matches!(w.kind,WindowKind::DiscordStream | WindowKind::DiscordPopout),
                        "minimized":w.minimized,
                        "width":w.width, "height":w.height,
                        "confirmedPiP":w.strict_match,"topmost":w.topmost,
                        "style":format!("0x{:08X}",engine::exstyle(w.handle as isize as HWND))})
                    })
                    .collect();
                json!({"ok":true, "count":windows.len(), "windows":windows})
            }
            "status" => self.status(),
            "capture" | "select" => {
                let windows = engine::scan();
                let chosen = if arg.is_empty() || arg.eq_ignore_ascii_case("auto") {
                    windows.iter().find(|w| w.strict_match)
                } else {
                    let value = if let Some(raw) =
                        arg.strip_prefix("0x").or_else(|| arg.strip_prefix("0X"))
                    {
                        i64::from_str_radix(raw, 16).ok()
                    } else {
                        arg.parse::<i64>()
                            .ok()
                            .or_else(|| i64::from_str_radix(arg, 16).ok())
                    };
                    windows.iter().find(|w| Some(w.handle) == value)
                };
                match chosen {
                    None => {
                        json!({"ok":false,"error":"No matching video pop-out. Restore the window and run pipctl.ps1 list"})
                    }
                    Some(w) if !w.strict_match => {
                        json!({"ok":false,"error":"CLI capture requires a visible, verified PiP or Discord pop-out"})
                    }
                    Some(w) => match self.capture_verified(w) {
                        Ok(()) => self.status(),
                        Err(e) => json!({"ok":false,"error":e}),
                    },
                }
            }
            "on" => {
                self.set_click(true);
                self.status()
            }
            "off" => {
                self.set_click(false);
                self.status()
            }
            "toggle" => {
                self.toggle();
                self.status()
            }
            "opacity" => match arg.parse::<i32>() {
                Ok(v) if (20..=100).contains(&v) => {
                    self.opacity(v);
                    self.status()
                }
                _ => json!({"ok":false,"error":"Expected opacity 20..100"}),
            },
            "restore" | "release" => {
                self.restore();
                if self.engine.count() != 0 {
                    json!({"ok":false,"error":"Some windows could not be restored"})
                } else {
                    self.status()
                }
            }
            "refresh" => {
                self.scan(true);
                self.status()
            }
            "show" => {
                self.event(ctx, Event::Show);
                self.status()
            }
            "verify" => {
                let handles = self.engine.handles();
                let windows: Vec<_> = engine::scan()
                    .iter()
                    .filter(|w| handles.contains(&w.handle))
                    .map(|w| {
                        let flags = engine::exstyle(w.handle as isize as HWND);
                        json!({"handle":format!("0x{:X}",w.handle),
                            "validWindow":engine::same_window(w),"title":w.title,
                            "style":format!("0x{:08X}",flags),
                            "transparent":flags & 0x20 != 0,
                            "layered":flags & 0x80000 != 0,
                            "nonActivating":flags & 0x8000000 != 0})
                    })
                    .collect();
                json!({"ok":true, "clickThrough":self.engine.click_through,
                    "capturedCount":self.engine.count(),"windows":windows})
            }
            _ => {
                json!({"ok":false,"error":"Unknown command. Use list, status, capture, on, off, toggle, opacity, verify, refresh, restore, show"})
            }
        }
    }
    fn status(&self) -> Value {
        json!({"ok":true,"app":"PiP ClickThrough","runtime":"Rust/egui",
            "clickThrough":self.engine.click_through, "autoDetect":self.settings.auto_detect,
            "autoEnable":self.settings.auto_enable, "opacity":self.engine.opacity,
            "autoDiscordStreams":self.settings.auto_discord_streams,
            "capturedCount":self.engine.count(),
            "capturedHandles":self.engine.handles().iter().map(|h|format!("0x{h:X}")).collect::<Vec<_>>(),
            "realPiPCount":engine::scan().iter().filter(|w|w.kind==WindowKind::BrowserPip && w.strict_match).count(),
            "discordStreamCount":engine::scan().iter().filter(|w|matches!(w.kind,WindowKind::DiscordStream | WindowKind::DiscordPopout)).count(),
            "selected":self.selected.map(|h|format!("0x{h:X}"))})
    }

    fn sidebar(&mut self, root: &mut egui::Ui) {
        Panel::left("navigation")
            .resizable(false)
            .default_size(218.)
            .min_size(218.)
            .max_size(218.)
            .frame(
                Frame::new()
                    .fill(SIDEBAR)
                    .inner_margin(Margin::symmetric(19, 24)),
            )
            .show(root, |ui| {
                ui.horizontal(|ui| {
                    let (rect, _) = ui.allocate_exact_size(Vec2::splat(44.), Sense::hover());
                    let p = ui.painter();
                    p.rect_filled(rect, 13., CYAN);
                    p.rect_filled(rect.shrink(11.), 5., SIDEBAR);
                    p.rect_stroke(
                        rect.shrink(6.),
                        6.,
                        Stroke::new(2., SIDEBAR),
                        StrokeKind::Inside,
                    );
                    ui.vertical(|ui| {
                        ui.add_space(2.);
                        ui.label(heading("PiP Control", 18.));
                        ui.label(secondary("CLICK-THROUGH", 10.));
                    });
                });
                ui.add_space(37.);
                ui.label(secondary("WORKSPACE", 11.).strong());
                ui.add_space(12.);
                for (page, name) in [
                    (Page::Overview, "Overview"),
                    (Page::Windows, "Video windows"),
                    (Page::Settings, "Preferences"),
                ] {
                    let active = self.page == page;
                    let mut control = Button::new(
                        RichText::new(format!("    {name}"))
                            .size(14.)
                            .color(if active { CYAN } else { MUTED }),
                    )
                    .fill(if active {
                        Color32::from_rgb(21, 57, 68)
                    } else {
                        Color32::TRANSPARENT
                    })
                    .stroke(Stroke::NONE)
                    .corner_radius(10)
                    .min_size(Vec2::new(ui.available_width(), 45.));
                    if active {
                        control = control.stroke(Stroke::new(1., CYAN_DARK));
                    }
                    if ui.add(control).clicked() {
                        self.page = page;
                    }
                    ui.add_space(6.);
                }
                ui.add_space(ui.available_height().max(165.) - 144.);
                ui.separator();
                ui.add_space(14.);
                ui.horizontal(|ui| {
                    ui.label(RichText::new("LIVE").color(GREEN));
                    ui.label(secondary("NATIVE WINDOWS ENGINE", 10.).strong());
                });
                ui.add_space(5.);
                ui.label(secondary("Rust v2.1.1  •  Local only", 11.));
            });
    }

    fn header(&mut self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            ui.vertical(|ui| {
                ui.label(secondary("YOUR PERSONAL VIDEO WORKSPACE", 11.).strong());
                ui.add_space(5.);
                let title = match self.page {
                    Page::Overview => "Control center",
                    Page::Windows => "PiP windows",
                    Page::Settings => "Preferences",
                };
                ui.label(heading(title, 29.));
                ui.add_space(2.);
                ui.label(secondary(
                    "Effortless picture-in-picture, built for Windows.",
                    13.,
                ));
            });
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                Frame::new()
                    .fill(PANEL)
                    .stroke(Stroke::new(1., BORDER))
                    .corner_radius(20)
                    .inner_margin(Margin::symmetric(14, 8))
                    .show(ui, |ui| {
                        ui.horizontal(|ui| {
                            ui.label(RichText::new("LIVE").color(GREEN).size(11.));
                            ui.label(RichText::new("MONITORING").size(11.).color(WHITE).strong());
                        });
                    });
            });
        });
        ui.add_space(27.);
    }

    fn hero(&mut self, ui: &mut egui::Ui) {
        let width = ui.available_width();
        let (rect, _) = ui.allocate_exact_size(Vec2::new(width, 167.), Sense::hover());
        let painter = ui.painter().with_clip_rect(rect);
        painter.rect_filled(rect, 18., Color32::from_rgb(16, 51, 67));
        for i in 0..54 {
            let band = rect.width() * i as f32 / 54.;
            let color = Color32::from_rgba_unmultiplied(26, 173, 177, ((54 - i) * 2).min(55) as u8);
            painter.line_segment(
                [
                    rect.left_top() + egui::vec2(band, 14.),
                    rect.left_bottom() + egui::vec2(band, -14.),
                ],
                Stroke::new(rect.width() / 54. + 1., color),
            );
        }
        painter.rect_stroke(rect, 18., Stroke::new(1., CYAN_DARK), StrokeKind::Inside);
        let circle = egui::pos2(rect.right() - 92., rect.center().y);
        painter.circle_filled(circle, 66., Color32::from_rgb(21, 92, 108));
        painter.circle_stroke(
            circle,
            50.,
            Stroke::new(2., Color32::from_rgb(76, 183, 189)),
        );
        let play = [
            egui::pos2(circle.x - 12., circle.y - 20.),
            egui::pos2(circle.x - 12., circle.y + 20.),
            egui::pos2(circle.x + 24., circle.y),
        ];
        painter.add(egui::Shape::convex_polygon(
            play.to_vec(),
            CYAN,
            Stroke::NONE,
        ));
        let x = rect.left() + 29.;
        painter.text(
            egui::pos2(x, rect.top() + 27.),
            Align2::LEFT_TOP,
            "PICTURE-IN-PICTURE  /  REIMAGINED",
            FontId::proportional(11.),
            CYAN,
        );
        painter.text(
            egui::pos2(x, rect.top() + 54.),
            Align2::LEFT_TOP,
            if self.engine.count() > 0 {
                "Your video is connected."
            } else {
                "Your video. Your flow."
            },
            FontId::proportional(25.),
            WHITE,
        );
        painter.text(
            egui::pos2(x, rect.top() + 99.),
            Align2::LEFT_TOP,
            "Watch on top of everything. Click through when you need to.",
            FontId::proportional(13.),
            Color32::from_rgb(191, 222, 225),
        );
        painter.text(
            egui::pos2(x, rect.top() + 123.),
            Align2::LEFT_TOP,
            "Zero extensions  •  Global shortcuts  •  Native Rust",
            FontId::proportional(12.),
            Color32::from_rgb(141, 205, 211),
        );
        ui.add_space(22.);
    }

    fn source_card(&mut self, ui: &mut egui::Ui) {
        tile().show(ui, |ui| {
            ui.set_min_height(182.);
            ui.horizontal(|ui| {
                ui.label(heading("Video source", 17.));
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    ui.label(
                        RichText::new(format!("{} found", self.candidates.len()))
                            .color(CYAN)
                            .size(12.),
                    );
                });
            });
            ui.add_space(4.);
            ui.label(secondary("Chrome PiP or Discord stream pop-out", 12.));
            ui.add_space(15.);
            let selected_label = self
                .selected
                .and_then(|h| self.candidates.iter().find(|w| w.handle == h))
                .map(|w| {
                    let name = engine::browser(&w.process_name);
                    format!(
                        "{}  •  {}",
                        name,
                        match (w.kind, w.strict_match) {
                            (WindowKind::DiscordStream, true) => "Pinned stream",
                            (WindowKind::DiscordStream, false) => "Unpinned stream / manual",
                            (WindowKind::DiscordPopout, true) => "Discord pop-out",
                            (WindowKind::DiscordPopout, false) => "Minimized / manual pop-out",
                            (_, true) => "Verified PiP",
                            _ => "Manual candidate",
                        }
                    )
                })
                .unwrap_or_else(|| "No PiP window detected".to_owned());
            ComboBox::from_id_salt("pip-window")
                .selected_text(selected_label)
                .width(ui.available_width() - 18.)
                .show_ui(ui, |ui| {
                    for w in &self.candidates {
                        ui.selectable_value(&mut self.selected, Some(w.handle), w.label());
                    }
                });
            ui.add_space(14.);
            ui.horizontal(|ui| {
                if ui
                    .add_enabled(
                        self.selected.is_some(),
                        action_button("Capture window", true),
                    )
                    .clicked()
                {
                    self.capture_selected();
                }
                if ui.add(small_button("Refresh")).clicked() {
                    self.scan(true);
                }
            });
        });
    }
    fn control_card(&mut self, ui: &mut egui::Ui) {
        tile().show(ui, |ui| {
            ui.set_min_height(182.);
            ui.horizontal(|ui| {
                ui.label(heading("Quick controls", 17.));
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    ui.label(
                        RichText::new(if self.engine.click_through {
                            "PASS-THROUGH"
                        } else {
                            "INTERACTIVE"
                        })
                        .color(if self.engine.click_through {
                            GREEN
                        } else {
                            CYAN
                        })
                        .size(10.)
                        .strong(),
                    );
                });
            });
            ui.add_space(4.);
            ui.label(secondary("Switch modes instantly, anywhere", 12.));
            ui.add_space(20.);
            ui.horizontal(|ui| {
                if ui
                    .add(action_button(
                        if self.engine.click_through {
                            "Interactive mode"
                        } else {
                            "Click-through"
                        },
                        true,
                    ))
                    .clicked()
                {
                    self.toggle();
                }
                if ui.add(small_button("Restore all")).clicked() {
                    self.restore();
                }
            });
            ui.add_space(17.);
            ui.horizontal(|ui| {
                ui.label(
                    RichText::new("Ctrl + Shift + F10")
                        .color(CYAN)
                        .size(12.)
                        .strong(),
                );
                ui.label(secondary("to toggle anytime", 11.));
            });
        });
    }
    fn preference_toggle(
        &mut self,
        ui: &mut egui::Ui,
        label: &str,
        desc: &str,
        value: bool,
    ) -> bool {
        let mut new = value;
        ui.horizontal(|ui| {
            ui.vertical(|ui| {
                ui.label(heading(label, 14.));
                ui.add_space(3.);
                ui.label(secondary(desc, 12.));
            });
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                let bg = if value { CYAN_DARK } else { PANEL_LIGHT };
                let fg = if value { CYAN } else { MUTED };
                if ui
                    .add(
                        Button::new(
                            RichText::new(if value { "ON" } else { "OFF" })
                                .size(11.)
                                .strong()
                                .color(fg),
                        )
                        .fill(bg)
                        .stroke(Stroke::new(1., if value { CYAN_DARK } else { BORDER }))
                        .corner_radius(18)
                        .min_size(Vec2::new(61., 32.)),
                    )
                    .clicked()
                {
                    new = !value;
                }
            });
        });
        new
    }
    fn preferences_card(&mut self, ui: &mut egui::Ui) {
        tile().show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.label(heading("Preferences", 17.));
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    ui.label(secondary("PERSONALIZE YOUR WORKFLOW", 10.).strong());
                });
            });
            ui.add_space(15.);
            ui.columns(3, |columns| {
                let auto = self.preference_toggle(
                    &mut columns[0],
                    "Auto-detect",
                    "Find video windows",
                    self.settings.auto_detect,
                );
                if auto != self.settings.auto_detect {
                    self.settings.auto_detect = auto;
                    self.save();
                    self.scan(true);
                }
                let enable = self.preference_toggle(
                    &mut columns[1],
                    "Auto-enable",
                    "Activate click-through",
                    self.settings.auto_enable,
                );
                if enable != self.settings.auto_enable {
                    self.set_click(enable);
                }
                let on_top = self.preference_toggle(
                    &mut columns[2],
                    "Always on top",
                    "Keep video above apps",
                    self.settings.always_on_top,
                );
                if on_top != self.settings.always_on_top {
                    self.settings.always_on_top = on_top;
                    self.engine.set_top(on_top);
                    self.save();
                }
            });
            ui.add_space(17.);
            ui.separator();
            ui.add_space(13.);
            let discord = self.preference_toggle(
                ui,
                "Discord stream pop-outs",
                "Detect Discord, PTB and Canary pop-outs (not chat windows)",
                self.settings.auto_discord_streams,
            );
            if discord != self.settings.auto_discord_streams {
                self.settings.auto_discord_streams = discord;
                self.save();
                self.scan(true);
            }
            ui.add_space(13.);
            ui.separator();
            ui.add_space(13.);
            ui.horizontal(|ui| {
                ui.vertical(|ui| {
                    ui.label(heading("Video opacity", 14.));
                    ui.add_space(3.);
                    ui.label(secondary("Make the video less distracting", 12.));
                });
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    let mut opacity = self.engine.opacity;
                    ui.label(
                        RichText::new(format!("{}%", opacity))
                            .color(CYAN)
                            .strong()
                            .size(14.),
                    );
                    let slider = Slider::new(&mut opacity, 20..=100)
                        .show_value(false)
                        .step_by(5.);
                    let resp = ui.add_sized([210., 22.], slider);
                    if resp.changed() {
                        self.opacity(opacity);
                    }
                });
            });
        });
    }
    fn activity_card(&self, ui: &mut egui::Ui) {
        tile().show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.label(RichText::new("LIVE").color(GREEN).size(11.));
                ui.label(heading("Activity", 14.));
                ui.add_space(8.);
                ui.label(secondary(self.activity.clone(), 12.));
            });
        });
    }
    fn overview(&mut self, ui: &mut egui::Ui) {
        self.hero(ui);
        let available = ui.available_width();
        ui.columns(2, |cols| {
            cols[0].set_width(available / 2. - 7.);
            cols[1].set_width(available / 2. - 7.);
            self.source_card(&mut cols[0]);
            self.control_card(&mut cols[1]);
        });
        ui.add_space(19.);
        self.preferences_card(ui);
        ui.add_space(17.);
        self.activity_card(ui);
    }
    fn windows_page(&mut self, ui: &mut egui::Ui) {
        self.source_card(ui);
        ui.add_space(16.);
        tile().show(ui, |ui| {
            ui.label(heading("Detected windows", 17.));
            ui.add_space(7.);
            if self.candidates.is_empty() {
                ui.label(secondary("No video windows found. Start Chrome PiP or pop out a Discord stream and pin Stay On Top.", 13.));
            }
            for w in &self.candidates {
                ui.horizontal(|ui| {
                    ui.label(RichText::new(if w.strict_match {"VERIFIED"}else{"MANUAL"})
                        .size(11.).color(if w.strict_match{GREEN}else{MUTED}));
                    ui.label(heading(format!("{}  •  {}",
                        engine::browser(&w.process_name),w.title),13.));
                    ui.with_layout(Layout::right_to_left(Align::Center),|ui| {
                        ui.label(secondary(format!("{} × {}",w.width,w.height),12.));
                    });
                });
                ui.add_space(9.);
            }
        });
    }
    fn settings_page(&mut self, ui: &mut egui::Ui) {
        self.preferences_card(ui);
        ui.add_space(17.);
        tile().show(ui, |ui| {
            ui.label(heading("Keyboard shortcuts", 17.));
            ui.add_space(10.);
            for (key, action) in [
                ("Ctrl + Shift + F9", "Open control center"),
                ("Ctrl + Shift + F10", "Toggle click-through / interactive"),
                ("Ctrl + Shift + Page Up", "Increase video opacity"),
                ("Ctrl + Shift + Page Down", "Decrease video opacity"),
                ("Ctrl + Shift + F12", "Restore all windows"),
            ] {
                ui.horizontal(|ui| {
                    ui.label(RichText::new(key).color(CYAN).monospace().size(13.));
                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                        ui.label(secondary(action, 12.));
                    });
                });
                ui.add_space(10.);
            }
        });
    }
    fn manual_dialog(&mut self, ctx: &egui::Context) {
        let Some(w) = self.manual_confirm.clone() else {
            return;
        };
        egui::Window::new("Confirm manual capture")
            .collapsible(false)
            .resizable(false)
            .fixed_size([435., 150.])
            .anchor(Align2::CENTER_CENTER, [0., 0.])
            .show(ctx, |ui| {
                ui.label(heading("This window isn't pinned/verified.", 17.));
                ui.add_space(8.);
                ui.label(secondary(w.label(), 12.));
                ui.add_space(12.);
                ui.horizontal(|ui| {
                    if ui.add(action_button("Capture anyway", true)).clicked() {
                        self.manual_confirm = None;
                        if let Err(e) = self.capture_verified(&w) {
                            self.log(format!("Capture failed: {e}"));
                        }
                    }
                    if ui.add(small_button("Cancel")).clicked() {
                        self.manual_confirm = None;
                    }
                });
            });
    }
}
impl eframe::App for Dashboard {
    fn on_exit(&mut self, _gl: Option<&eframe::glow::Context>) {
        let _ = self.engine.restore_all();
        service::shutdown();
    }
    fn logic(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        while let Ok(event) = self.rx.try_recv() {
            self.event(ctx, event);
        }
        if ctx.input(|input| input.viewport().close_requested())
            && !self.exiting
            && self.settings.minimize_to_tray
        {
            ctx.send_viewport_cmd(ViewportCommand::CancelClose);
            ctx.send_viewport_cmd(ViewportCommand::Visible(false));
        }
        self.scan(false);
        ctx.request_repaint_after(Duration::from_millis(950));
    }
    fn ui(&mut self, root: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let ctx = root.ctx().clone();
        self.sidebar(root);
        CentralPanel::default()
            .frame(
                Frame::new()
                    .fill(BG)
                    .inner_margin(Margin::symmetric(28, 23)),
            )
            .show(root, |ui| {
                egui::ScrollArea::vertical()
                    .auto_shrink([false, false])
                    .show(ui, |ui| {
                        self.header(ui);
                        match self.page {
                            Page::Overview => self.overview(ui),
                            Page::Windows => self.windows_page(ui),
                            Page::Settings => self.settings_page(ui),
                        }
                        ui.add_space(18.);
                        ui.horizontal(|ui| {
                            ui.label(secondary("PiP ClickThrough 2.1.1  •  Built with Rust", 10.));
                            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                ui.label(secondary("PRIVATE  ·  ON-DEVICE", 10.));
                            });
                        });
                    });
            });
        self.manual_dialog(&ctx);
    }
}

pub fn run() -> io::Result<()> {
    let native = eframe::NativeOptions {
        viewport: ViewportBuilder::default()
            .with_title("PiP ClickThrough")
            .with_inner_size([1090., 765.])
            .with_min_inner_size([970., 710.]),
        ..Default::default()
    };
    eframe::run_native(
        "PiP ClickThrough",
        native,
        Box::new(|cc| {
            let mut visuals = egui::Visuals::dark();
            visuals.window_fill = BG;
            visuals.panel_fill = BG;
            visuals.override_text_color = Some(WHITE);
            visuals.widgets.noninteractive.bg_fill = PANEL;
            visuals.widgets.inactive.bg_fill = PANEL_LIGHT;
            visuals.widgets.hovered.bg_fill = CYAN_DARK;
            visuals.widgets.active.bg_fill = CYAN_DARK;
            visuals.selection.bg_fill = CYAN_DARK;
            visuals.selection.stroke.color = CYAN;
            visuals.widgets.noninteractive.corner_radius = egui::CornerRadius::same(9);
            visuals.widgets.inactive.corner_radius = egui::CornerRadius::same(9);
            cc.egui_ctx.set_visuals(visuals);
            cc.egui_ctx.style_mut_of(egui::Theme::Dark, |style| {
                style.spacing.item_spacing = egui::vec2(10., 8.);
            });
            let (tx, rx) = mpsc::channel();
            service::spawn(tx, cc.egui_ctx.clone());
            let app = Dashboard::new(rx)
                .map_err(|e| -> Box<dyn std::error::Error + Send + Sync> { Box::new(e) })?;
            Ok(Box::new(app))
        }),
    )
    .map_err(|e| io::Error::other(e.to_string()))
}
