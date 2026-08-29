# 8x8 ToF Gesture Recognition (Arduino + Mac/PC)

This document details the complete operational architecture, mathematical workflow, and C++/Python implementation for offloading 8x8 Time-of-Flight (ToF) gesture processing to a Mac/PC host while utilizing an Arduino UNO R4 Minima as an edge acquisition device.

---

## 1. System Architecture & Processing Pipeline

The system is split into **Edge Acquisition** (Arduino) and **Host Processing** (Mac/PC). This division isolates high-frequency hardware I/O on the microcontroller while taking advantage of the host's floating-point performance for Hidden Markov Model (HMM) evaluation.

```
+-----------------------------------------------------------------------------------+
|                            1. EDGE DEVICE (Arduino UNO R4)                        |
|                                                                                   |
|  [ 8x8 ToF Sensor ] --(I2C, 30Hz)--> [ Spatial Median Filter ]                    |
|                                                     |                             |
|                                                     v                             |
|                                          [ Distance Bounds Mask ]                 |
|                                                     |                             |
|                                                     v                             |
|                                         [ USB Serial Transmitter ]                |
+-----------------------------------------------------------------------------------+
                                                      |
                                                      | USB Serial (115200 Baud)
                                                      | 64-dim Float CSV
                                                      v
+-----------------------------------------------------------------------------------+
|                                2. HOST SYSTEM (Mac / PC)                          |
|                                                                                   |
|  [ Serial Receiver ] --> [ Ring Buffer (10 Frames) ] --> [ GMM-HMM Inference ]    |
|                                                                 |                 |
|                                                                 v                 |
|                                                     [ Rejection Threshold ]       |
|                                                                 |                 |
|                                                                 v                 |
|                                                     [ Classification Output ]     |
+-----------------------------------------------------------------------------------+
```

---

## 2. Step-by-Step Mathematical & Operational Workflow

### Step 1: Edge Data Preprocessing (Arduino)

1. **Spatial Median Filtering (3x3):** Suppresses single-pixel sensor salt-and-pepper noise without blurring edge boundaries across the 8x8 depth grid.
2. **Range Masking:** Rejects values outside the operational region of interest ($150\text{ mm} \le d_{r,c} \le 400\text{ mm}$). Out-of-bounds pixels are set to $0.0\text{ mm}$.
3. **Serialization:** Packs the 64 depth values into a comma-separated ASCII stream followed by `\r\n` and sends it via USB Serial at ~30 FPS.

### Step 2: Temporal Buffering & Scoring (Mac/PC)

1. **Sliding Window Buffer:** Incoming 64-dimensional vectors enter a First-In-First-Out (FIFO) queue of length $N = 10$ frames ($\approx 330\text{ ms}$ observation window).
2. **Log-Likelihood Evaluation:** When full, observation sequence $O = (x_1, x_2, \dots, x_{10})$ is passed to all gesture-specific GMM-HMM models ($\lambda_{Rock}, \lambda_{Paper}, \dots$).
3. **Model Formulation:** Each gesture class $\lambda_g$ is modeled as a 2-state left-to-right HMM with 2 Gaussian components per state:

$$P(O \mid \lambda_g) = \sum_{q} \pi_{q_1} b_{q_1}(\mathbf{x}_1) \prod_{t=2}^{T} a_{q_{t-1} q_t} b_{q_t}(\mathbf{x}_t)$$

where:

$$b_i(\mathbf{x}_t) = \sum_{m=1}^{M} c_{im} \mathcal{N}(\mathbf{x}_t; \boldsymbol{\mu}_{im}, \mathbf{\Sigma}_{im})$$

4. **Threshold Rejection:** Evaluates the highest log-likelihood score:

$$\hat{g} = \arg\max_{g} \log P(O \mid \lambda_g)$$

If $\log P(O \mid \lambda_{\hat{g}}) < \tau$ (where $\tau = -5000.0$), the output is classified as `REJECTED` (no valid gesture detected).

---

## 3. Arduino UNO R4 Minima Code (Edge Processing)

This sketch handles spatial filtering and range masking locally before sending pre-filtered data to the host.

```cpp
#include <Arduino.h>

constexpr int GRID_SIZE = 8;
constexpr int DIM = 64;

// Preprocessing: 3x3 Median Filter & Range Masking
void preprocessFrame(const float* raw_input, float* processed_output, 
                     float min_dist = 150.0f, float max_dist = 400.0f) 
{
    float grid[GRID_SIZE][GRID_SIZE];
    float filtered[GRID_SIZE][GRID_SIZE];

    for (int i = 0; i < DIM; ++i) {
        grid[i / GRID_SIZE][i % GRID_SIZE] = raw_input[i];
    }

    // 3x3 Median Filter using stack allocation
    for (int r = 0; r < GRID_SIZE; ++r) {
        for (int c = 0; c < GRID_SIZE; ++c) {
            float window[9];
            int win_size = 0;

            for (int dr = -1; dr <= 1; ++dr) {
                for (int dc = -1; dc <= 1; ++dc) {
                    int nr = r + dr;
                    int nc = c + dc;
                    if (nr >= 0 && nr < GRID_SIZE && nc >= 0 && nc < GRID_SIZE) {
                        window[win_size++] = grid[nr][nc];
                    }
                }
            }

            // Insertion sort for microcontroller efficiency
            for (int i = 1; i < win_size; ++i) {
                float key = window[i];
                int j = i - 1;
                while (j >= 0 && window[j] > key) {
                    window[j + 1] = window[j];
                    j--;
                }
                window[j + 1] = key;
            }
            filtered[r][c] = window[win_size / 2];
        }
    }

    // Distance bounds masking
    for (int r = 0; r < GRID_SIZE; ++r) {
        for (int c = 0; c < GRID_SIZE; ++c) {
            float val = filtered[r][c];
            int idx = r * GRID_SIZE + c;
            processed_output[idx] = (val >= min_dist && val <= max_dist) ? val : 0.0f;
        }
    }
}

void setup() {
    Serial.begin(115200);
    while (!Serial) delay(10);
}

void loop() {
    float raw_tof_frame[DIM];
    float processed_frame[DIM];

    // Replace with ToF driver call (e.g., VL53L5CX getRangingData)
    for (int i = 0; i < DIM; ++i) {
        raw_tof_frame[i] = 200.0f + random(-10, 10); // Simulated range data
    }

    // Execute edge preprocessing
    preprocessFrame(raw_tof_frame, processed_frame);

    // Transmit 64 comma-separated values to host
    for (int i = 0; i < DIM; ++i) {
        Serial.print(processed_frame[i], 1);
        if (i < DIM - 1) Serial.print(",");
    }
    Serial.println();

    delay(33); // ~30 Hz sampling rate
}
```

---

## 4. Mac / PC Python Code (Training & Real-Time Inference)

This script manages training, live serial parsing, temporal queuing, and GMM-HMM inference.

```python
import serial
import numpy as np
import collections
import time
from hmmlearn.hmm import GMMHMM

# Serial Configuration (Adjust port for your OS)
# Mac: '/dev/tty.usbmodem14101' or '/dev/tty.usbmodem*'
# Windows: 'COM3'
SERIAL_PORT = '/dev/tty.usbmodem14101'
BAUD_RATE = 115200

def train_gmm_hmm_models(dataset: dict) -> dict:
    """
    Trains a GMM-HMM for each gesture class.
    
    dataset structure:
      {'Rock': [seq1, seq2, ...], 'Paper': [seq1, seq2, ...]}
    where each sequence is an ndarray of shape (n_frames, 64).
    """
    models = {}
    for gesture_name, sequences in dataset.items():
        # n_components=2 (Moving -> Pose), n_mix=2 (Palm, Wrist)
        model = GMMHMM(
            n_components=2, 
            n_mix=2, 
            covariance_type="diag", 
            random_state=42,
            n_iter=100
        )
        
        X_concat = np.vstack(sequences)
        lengths = [len(seq) for seq in sequences]
        
        model.fit(X_concat, lengths=lengths)
        models[gesture_name] = model
        print(f"Model for '{gesture_name}' successfully trained.")
        
    return models

def run_realtime_inference():
    try:
        ser = serial.Serial(SERIAL_PORT, BAUD_RATE, timeout=1)
        print(f"Connected to port: {SERIAL_PORT}")
    except Exception as e:
        print(f"Serial connection error: {e}")
        return

    BUF_SIZE = 10
    
    # Mock dataset for setup initialization (Replace with actual recorded CSV sequences)
    mock_dataset = {
        'Rock': [np.full((BUF_SIZE, 64), 200.0) for _ in range(5)],
        'Paper': [np.full((BUF_SIZE, 64), 180.0) for _ in range(5)],
    }
    
    models = train_gmm_hmm_models(mock_dataset)
    frame_buffer = collections.deque(maxlen=BUF_SIZE)
    REJECTION_THRESHOLD = -5000.0

    print("\n--- Real-Time Inference Loop Active (Press Ctrl+C to terminate) ---")
    
    while True:
        if ser.in_waiting:
            line = ser.readline().decode('utf-8').strip()
            try:
                values = [float(x) for x in line.split(',')]
                if len(values) == 64:
                    frame_buffer.append(values)
                    
                    # Evaluate when ring buffer is saturated (10 frames)
                    if len(frame_buffer) == BUF_SIZE:
                        input_sequence = np.array(frame_buffer)
                        scores = {}
                        
                        for gesture_name, model in models.items():
                            try:
                                scores[gesture_name] = model.score(input_sequence)
                            except Exception:
                                scores[gesture_name] = -np.inf
                        
                        best_gesture = max(scores, key=scores.get)
                        max_score = scores[best_gesture]
                        
                        # Rejection threshold check
                        if max_score < REJECTION_THRESHOLD:
                            result = "REJECTED"
                        else:
                            result = best_gesture
                            
                        print(f"\rRecognized: {result:<10} | Score: {max_score:.1f}", end="")
            except ValueError:
                pass

if __name__ == "__main__":
    run_realtime_inference()
```

---

## 5. Architectural Advantages

* **Hyperparameter Tuning:** Modifying Gaussian mixture counts ($M$), hidden states ($N$), or log-likelihood rejection thresholds requires zero firmware flashing—all updates occur natively in Python.
* **Bandwidth Optimization:** Applying spatial filtering directly on the Arduino eliminates payload noise, reducing USB transmission packet variance.
* **Integrated Data Pipeline:** The host script can directly stream arriving serial arrays into CSV datasets, allowing seamless dataset expansion and model retraining.
