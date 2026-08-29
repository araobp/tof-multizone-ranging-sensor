import sys
import time
import matplotlib.patheffects as path_effects
import matplotlib.pyplot as plt
from matplotlib.widgets import Button, Slider
import numpy as np
import serial
import serial.tools.list_ports

# --- Configuration ---
BAUD_RATE = 115200
ROWS, COLS = 8, 8
NUM_PIXELS = ROWS * COLS

BEGIN_BYTE = b"\xfe"
END_BYTE = b"\xff"

# Available Matplotlib Colormaps (Reversed so closer distances are brighter/warmer)
COLORMAP_OPTIONS = ("jet_r", "hot_r", "inferno_r", "plasma_r", "coolwarm_r", "viridis_r")


def auto_find_port():
    """Automatically finds the Arduino / Serial port across Mac, Linux, and Windows."""
    ports = list(serial.tools.list_ports.comports())
    if not ports:
        return None

    # Common keywords in Arduino/USB-Serial driver names
    target_keywords = [
        "arduino",
        "usb",
        "ch340",
        "cp210",
        "ftdi",
        "usbmodem",
        "usbserial",
        "acm",
    ]

    # Prioritize ports matching our keywords
    for port in ports:
        port_info = (
            f"{port.device} {port.description} {port.hwid}".lower()
        )
        if any(keyword in port_info for keyword in target_keywords):
            print(f"Detected device: {port.device} ({port.description})")
            return port.device

    # Fallback: Just return the first available port if no keywords matched
    print(f"No specific matching device found. Trying first available port: {ports[0].device}")
    return ports[0].device


def label_components(binary_grid):
    """8-connectivity flood fill connected components labeler for 8x8 matrix."""
    labels = np.zeros((ROWS, COLS), dtype=int)
    current_label = 0
    for r in range(ROWS):
        for c in range(COLS):
            if binary_grid[r, c] and labels[r, c] == 0:
                current_label += 1
                queue = [(r, c)]
                labels[r, c] = current_label
                while queue:
                    curr_r, curr_c = queue.pop(0)
                    for dr in (-1, 0, 1):
                        for dc in (-1, 0, 1):
                            nr, nc = curr_r + dr, curr_c + dc
                            if 0 <= nr < ROWS and 0 <= nc < COLS:
                                if binary_grid[nr, nc] and labels[nr, nc] == 0:
                                    labels[nr, nc] = current_label
                                    queue.append((nr, nc))
    return labels, current_label


def main():
    # 1. Dynamically find the port
    serial_port = auto_find_port()

    if not serial_port:
        print("Error: No serial ports detected. Please plug in your Arduino.")
        sys.exit(1)

    # 2. Initialize Serial Connection
    try:
        ser = serial.Serial(serial_port, BAUD_RATE, timeout=1.0)
        time.sleep(2)  # Wait for Arduino reboot
        ser.reset_input_buffer()
        print(f"Successfully connected to {serial_port} at {BAUD_RATE} baud.")
    except Exception as e:
        print(f"Error opening serial port {serial_port}: {e}")
        sys.exit(1)

    # 3. Setup Plotting Environment
    plt.ion()  # Turn on interactive mode
    fig, ax = plt.subplots(figsize=(6.5, 7.2))
    fig.canvas.manager.set_window_title("VL53L5CX Dynamic Distance Viewer")
    fig.subplots_adjust(left=0.12, right=0.88, top=0.92, bottom=0.27)

    grid_data = np.zeros((ROWS, COLS))

    # Set interpolation='gaussian' for smooth depth imaging.
    # Change to 'nearest' if you prefer a sharp pixel art style.
    im = ax.imshow(
        grid_data, cmap="jet_r", interpolation="gaussian", vmin=0, vmax=200
    )

    cbar = fig.colorbar(im, ax=ax)
    cbar.set_label("Distance (cm)")
    ax.axis("off")
    ax.set_title("VL53L5CX Real-Time Distance Viewer", fontsize=14, fontweight="bold", pad=12)

    # Selected colormap state (reversed colormap so close objects are warm/bright)
    selected_cmap = ["jet_r"]

    # Pre-create 8x8 text overlay grid for displaying numerical distance / labels per pixel
    text_grid = []
    for r in range(ROWS):
        row_texts = []
        for c in range(COLS):
            txt = ax.text(
                c, r, "", ha="center", va="center", color="white", fontsize=9, fontweight="bold"
            )
            txt.set_path_effects([path_effects.withStroke(linewidth=2, foreground="black")])
            txt.set_visible(False)
            row_texts.append(txt)
        text_grid.append(row_texts)

    # GUI widgets for controlling modes & Quit (All 8 buttons identical width = 0.20)
    btn_w = 0.20
    btn_h = 0.035
    x_col1, x_col2, x_col3, x_col4 = 0.065, 0.290, 0.515, 0.740
    y_row1, y_row2 = 0.200, 0.150

    ax_auto = fig.add_axes([x_col1, y_row1, btn_w, btn_h])
    ax_smooth = fig.add_axes([x_col2, y_row1, btn_w, btn_h])
    ax_diff = fig.add_axes([x_col3, y_row1, btn_w, btn_h])
    ax_diff_mag = fig.add_axes([x_col4, y_row1, btn_w, btn_h])

    ax_cmap_btn = fig.add_axes([x_col1, y_row2, btn_w, btn_h])
    ax_bin = fig.add_axes([x_col2, y_row2, btn_w, btn_h])
    ax_dist = fig.add_axes([x_col3, y_row2, btn_w, btn_h])
    ax_quit = fig.add_axes([x_col4, y_row2, btn_w, btn_h])

    ax_vmin = fig.add_axes([0.22, 0.092, 0.65, 0.025])
    ax_vmax = fig.add_axes([0.22, 0.042, 0.65, 0.025])

    # Colormap Dropdown Menu
    btn_cmap = Button(ax_cmap_btn, "Cmap: JET_R ▾", color="0.85", hovercolor="0.75")
    dropdown_axes = []
    dropdown_buttons = []
    dropdown_visible = [False]

    def toggle_dropdown(event):
        dropdown_visible[0] = not dropdown_visible[0]
        for ax_item in dropdown_axes:
            ax_item.set_visible(dropdown_visible[0])
        fig.canvas.draw_idle()

    btn_cmap.on_clicked(toggle_dropdown)

    for i, c_name in enumerate(COLORMAP_OPTIONS):
        ax_item = fig.add_axes([x_col1, 0.190 + i * 0.035, btn_w, 0.034])
        btn_item = Button(ax_item, c_name, color="0.95", hovercolor="#77dd77")
        ax_item.set_visible(False)
        dropdown_axes.append(ax_item)
        dropdown_buttons.append(btn_item)

        def make_select_callback(name):
            def on_select(event):
                selected_cmap[0] = name
                im.set_cmap(name)
                cbar.update_normal(im)
                btn_cmap.label.set_text(f"Cmap: {name.upper()} ▾")
                toggle_dropdown(None)
            return on_select

        btn_item.on_clicked(make_select_callback(c_name))

    # 1. Auto Scale Toggle Button
    auto_scale = [False]
    btn_auto = Button(ax_auto, "Auto Scale: OFF", color="0.85", hovercolor="0.75")

    def toggle_auto(event):
        auto_scale[0] = not auto_scale[0]
        if auto_scale[0]:
            btn_auto.label.set_text("Auto Scale: ON")
            btn_auto.color = "#77dd77"
            btn_auto.hovercolor = "#55bb55"
        else:
            btn_auto.label.set_text("Auto Scale: OFF")
            btn_auto.color = "0.85"
            btn_auto.hovercolor = "0.75"
        fig.canvas.draw_idle()

    btn_auto.on_clicked(toggle_auto)

    # 2. Smooth Interpolation Toggle Button (Gaussian vs Nearest 8x8 matrix)
    smooth_mode = [True]
    btn_smooth = Button(ax_smooth, "Smooth: ON", color="#77dd77", hovercolor="#55bb55")

    def toggle_smooth(event):
        smooth_mode[0] = not smooth_mode[0]
        if smooth_mode[0]:
            im.set_interpolation("gaussian")
            btn_smooth.label.set_text("Smooth: ON")
            btn_smooth.color = "#77dd77"
            btn_smooth.hovercolor = "#55bb55"
        else:
            im.set_interpolation("nearest")
            btn_smooth.label.set_text("Smooth: OFF")
            btn_smooth.color = "0.85"
            btn_smooth.hovercolor = "0.75"
        fig.canvas.draw_idle()

    btn_smooth.on_clicked(toggle_smooth)

    # 3. Show Distance Overlay Toggle Button
    show_dist = [False]
    btn_dist = Button(ax_dist, "Show Dist: OFF", color="0.85", hovercolor="0.75")

    def toggle_dist(event):
        show_dist[0] = not show_dist[0]
        if show_dist[0]:
            btn_dist.label.set_text("Show Dist: ON")
            btn_dist.color = "#77dd77"
            btn_dist.hovercolor = "#55bb55"
        else:
            btn_dist.label.set_text("Show Dist: OFF")
            btn_dist.color = "0.85"
            btn_dist.hovercolor = "0.75"
            for r in range(ROWS):
                for c in range(COLS):
                    text_grid[r][c].set_visible(False)
        fig.canvas.draw_idle()

    btn_dist.on_clicked(toggle_dist)

    # 4. Binarization Mode Toggle Button
    binarize_mode = [False]
    btn_bin = Button(ax_bin, "Binarize: OFF", color="0.85", hovercolor="0.75")

    def toggle_bin(event):
        binarize_mode[0] = not binarize_mode[0]
        if binarize_mode[0]:
            btn_bin.label.set_text("Binarize: ON")
            btn_bin.color = "#77dd77"
            btn_bin.hovercolor = "#55bb55"
            if diff_mode[0]:
                toggle_diff(None)  # mutually exclusive with diff mode
        else:
            btn_bin.label.set_text("Binarize: OFF")
            btn_bin.color = "0.85"
            btn_bin.hovercolor = "0.75"
            im.set_cmap(selected_cmap[0])
            cbar.update_normal(im)
        fig.canvas.draw_idle()

    btn_bin.on_clicked(toggle_bin)

    # 5. Frame Diff Mode Toggle Button
    diff_mode = [False]
    btn_diff = Button(ax_diff, "Diff: OFF", color="0.85", hovercolor="0.75")

    def toggle_diff(event):
        diff_mode[0] = not diff_mode[0]
        if diff_mode[0]:
            btn_diff.label.set_text("Diff: ON")
            btn_diff.color = "#77dd77"
            btn_diff.hovercolor = "#55bb55"
            if binarize_mode[0]:
                toggle_bin(None)  # mutually exclusive with binarize mode
        else:
            btn_diff.label.set_text("Diff: OFF")
            btn_diff.color = "0.85"
            btn_diff.hovercolor = "0.75"
            im.set_cmap(selected_cmap[0])
            cbar.update_normal(im)
        fig.canvas.draw_idle()

    btn_diff.on_clicked(toggle_diff)

    # 6. Diff Scale Toggle Button (1/100 vs 1x)
    diff_mag_001x = [True]
    btn_diff_mag = Button(
        ax_diff_mag, "Diff: 1/100", color="#77dd77", hovercolor="#55bb55"
    )

    def toggle_diff_mag(event):
        diff_mag_001x[0] = not diff_mag_001x[0]
        if diff_mag_001x[0]:
            btn_diff_mag.label.set_text("Diff: 1/100")
            btn_diff_mag.color = "#77dd77"
            btn_diff_mag.hovercolor = "#55bb55"
        else:
            btn_diff_mag.label.set_text("Diff: 1x")
            btn_diff_mag.color = "0.85"
            btn_diff_mag.hovercolor = "0.75"
        fig.canvas.draw_idle()

    btn_diff_mag.on_clicked(toggle_diff_mag)

    # Sliders setup without red initial line, with ticks
    s_vmin = Slider(ax_vmin, "Min Dist (cm)", 0, 200, valinit=0, valstep=1)
    s_vmax = Slider(ax_vmax, "Max Dist (cm)", 0, 200, valinit=200, valstep=1)

    # Remove red initial value vertical line
    s_vmin.vline.set_visible(False)
    s_vmax.vline.set_visible(False)

    # Add distance ticks (メモリ) to sliders
    ticks = list(range(0, 201, 20))
    ax_vmin.set_xticks(ticks)
    ax_vmin.tick_params(bottom=True, labelbottom=True, labelsize=7)
    ax_vmax.set_xticks(ticks)
    ax_vmax.tick_params(bottom=True, labelbottom=True, labelsize=7)

    # Turn off Auto Scale if user manually moves sliders
    def on_slider_manual(val):
        if auto_scale[0]:
            auto_scale[0] = False
            btn_auto.label.set_text("Auto Scale: OFF")
            btn_auto.color = "0.85"
            btn_auto.hovercolor = "0.75"
            fig.canvas.draw_idle()

    s_vmin.on_changed(on_slider_manual)
    s_vmax.on_changed(on_slider_manual)

    # Quit button setup
    btn_quit = Button(ax_quit, "Quit", color="#ff8888", hovercolor="#ff4444")
    is_running = [True]

    def on_quit(event):
        is_running[0] = False
        plt.close(fig)

    btn_quit.on_clicked(on_quit)

    prev_dist_grid = None

    print("Starting data visualization... Press Ctrl+C in terminal or click Quit to stop.")

    try:
        while is_running[0] and plt.fignum_exists(fig.number):
            # Flush accumulated stale bytes to guarantee zero-latency realtime stream
            if ser.in_waiting > (NUM_PIXELS + 2) * 2:
                ser.reset_input_buffer()

            # Sync with the stream: look for the BEGIN byte [0xFE]
            if ser.read(1) != BEGIN_BYTE:
                continue

            # Read the 64-pixel payload
            pixel_bytes = ser.read(NUM_PIXELS)
            if len(pixel_bytes) != NUM_PIXELS:
                continue

            # Verify the END byte [0xFF]
            if ser.read(1) != END_BYTE:
                continue

            # Parse byte data to 8x8 NumPy matrix (1 LSB = 1 cm)
            raw_grid = np.frombuffer(pixel_bytes, dtype=np.uint8).reshape(
                (ROWS, COLS)
            )
            dist_grid = raw_grid.astype(np.float32)

            # Rotate 180 degrees from previous orientation to fix upside-down display
            dist_grid = np.fliplr(dist_grid)

            # Determine display grid based on active mode (Normal, Binarize, or Diff)
            if binarize_mode[0]:
                bin_threshold = (s_vmin.val + s_vmax.val) / 2.0
                binary_mask = dist_grid <= bin_threshold
                labels, num_objects = label_components(binary_mask)
                display_grid = binary_mask.astype(np.float32)
                im.set_cmap("gray")
            elif diff_mode[0]:
                mag_factor = 0.01 if diff_mag_001x[0] else 1.0
                if prev_dist_grid is not None:
                    display_grid = (dist_grid - prev_dist_grid) * mag_factor
                else:
                    display_grid = np.zeros((ROWS, COLS), dtype=np.float32)
                prev_dist_grid = dist_grid.copy()
            else:
                display_grid = dist_grid
                prev_dist_grid = dist_grid.copy()

            # Update the graph dynamically
            im.set_data(display_grid)

            # Update distance/label overlay text if enabled
            if show_dist[0]:
                for r in range(ROWS):
                    for c in range(COLS):
                        if binarize_mode[0]:
                            lbl = labels[r, c]
                            if lbl > 0:
                                text_grid[r][c].set_text(str(lbl))
                                text_grid[r][c].set_visible(True)
                            else:
                                text_grid[r][c].set_visible(False)
                        elif diff_mode[0]:
                            val = display_grid[r, c]
                            if diff_mag_001x[0]:
                                text_grid[r][c].set_text(f"{val:+.2f}")
                            else:
                                text_grid[r][c].set_text(f"{val:+.1f}")
                            text_grid[r][c].set_visible(True)
                        else:
                            val = display_grid[r, c]
                            text_grid[r][c].set_text(f"{int(round(val))}")
                            text_grid[r][c].set_visible(True)
            else:
                for r in range(ROWS):
                    for c in range(COLS):
                        text_grid[r][c].set_visible(False)

            # Update colormap scale dynamically or via sliders (cm)
            if binarize_mode[0]:
                im.set_clim(vmin=0, vmax=1)
            elif diff_mode[0]:
                if auto_scale[0]:
                    vmin = float(display_grid.min())
                    vmax = float(display_grid.max())
                    if vmin == vmax:
                        vmax = vmin + 1.0
                else:
                    # Fixed diff scale range
                    if diff_mag_001x[0]:
                        vmin, vmax = -2.0, 2.0
                    else:
                        vmin, vmax = -50.0, 50.0
                im.set_clim(vmin=vmin, vmax=vmax)
            else:
                if auto_scale[0]:
                    vmin = float(dist_grid.min())
                    vmax = float(dist_grid.max())
                    if vmin == vmax:
                        vmax = vmin + 1.0

                    # Only update slider GUI handle if value has noticeably shifted to avoid redundant redraws
                    if abs(s_vmin.val - vmin) >= 1.0:
                        s_vmin.eventson = False
                        s_vmin.set_val(vmin)
                        s_vmin.eventson = True

                    if abs(s_vmax.val - vmax) >= 1.0:
                        s_vmax.eventson = False
                        s_vmax.set_val(vmax)
                        s_vmax.eventson = True
                else:
                    vmin = float(s_vmin.val)
                    vmax = float(s_vmax.val)
                    if vmin >= vmax:
                        vmax = vmin + 1.0

                im.set_clim(vmin=vmin, vmax=vmax)

            fig.canvas.draw_idle()
            fig.canvas.flush_events()

    except KeyboardInterrupt:
        print("\nStopping visualization...")
    finally:
        ser.close()
        plt.ioff()
        plt.close("all")
        print("Application closed successfully.")


if __name__ == "__main__":
    main()
