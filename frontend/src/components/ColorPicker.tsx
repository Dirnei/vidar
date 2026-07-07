import { useRef, useState, useCallback } from 'react';

interface Props {
  hue: number;
  saturation: number;
  onCommit: (h: number, s: number) => void;
}

export function ColorWheel({ hue, saturation, onCommit }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [dragging, setDragging] = useState(false);
  const size = 140;
  const radius = size / 2;

  const drawWheel = useCallback((ctx: CanvasRenderingContext2D) => {
    const imageData = ctx.createImageData(size, size);
    for (let y = 0; y < size; y++) {
      for (let x = 0; x < size; x++) {
        const dx = x - radius;
        const dy = y - radius;
        const dist = Math.sqrt(dx * dx + dy * dy);
        if (dist > radius) continue;
        const angle = (Math.atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        const sat = dist / radius;
        const [r, g, b] = hslToRgb(angle / 360, sat, 0.5);
        const i = (y * size + x) * 4;
        imageData.data[i] = r;
        imageData.data[i + 1] = g;
        imageData.data[i + 2] = b;
        imageData.data[i + 3] = 255;
      }
    }
    ctx.putImageData(imageData, 0, 0);
  }, []);

  const handleInteraction = (e: React.MouseEvent | React.TouchEvent) => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const rect = canvas.getBoundingClientRect();
    const clientX = 'touches' in e ? e.touches[0].clientX : e.clientX;
    const clientY = 'touches' in e ? e.touches[0].clientY : e.clientY;
    const x = clientX - rect.left - radius;
    const y = clientY - rect.top - radius;
    const dist = Math.min(Math.sqrt(x * x + y * y), radius);
    const angle = (Math.atan2(y, x) * 180 / Math.PI + 360) % 360;
    const sat = Math.round(dist / radius * 100);
    onCommit(Math.round(angle), sat);
  };

  const thumbAngle = hue * Math.PI / 180;
  const thumbDist = (saturation / 100) * radius;
  const thumbX = radius + Math.cos(thumbAngle) * thumbDist;
  const thumbY = radius + Math.sin(thumbAngle) * thumbDist;

  return (
    <div style={{ position: 'relative', width: size, height: size }}>
      <canvas
        ref={el => {
          if (el) {
            canvasRef.current = el;
            const ctx = el.getContext('2d');
            if (ctx) drawWheel(ctx);
          }
        }}
        width={size}
        height={size}
        style={{ borderRadius: '50%', cursor: 'crosshair' }}
        onMouseDown={e => { setDragging(true); handleInteraction(e); }}
        onMouseMove={e => { if (dragging) handleInteraction(e); }}
        onMouseUp={() => setDragging(false)}
        onMouseLeave={() => setDragging(false)}
        onTouchStart={e => { setDragging(true); handleInteraction(e); }}
        onTouchMove={e => { if (dragging) handleInteraction(e); }}
        onTouchEnd={() => setDragging(false)}
      />
      <div style={{
        position: 'absolute',
        left: thumbX - 8,
        top: thumbY - 8,
        width: 16,
        height: 16,
        borderRadius: '50%',
        border: '2px solid white',
        boxShadow: '0 0 4px rgba(0,0,0,0.5)',
        pointerEvents: 'none',
      }} />
    </div>
  );
}

interface ColorTempProps {
  value: number;
  min?: number;
  max?: number;
  // 'mireds' (default): Dreo-style scale where low values are cool and high values are warm,
  // displayed converted to Kelvin. 'kelvin': the raw value already is Kelvin (e.g. Loxone
  // tunable-white, 2700-6500K) where low values are warm and high values are cool — the inverse.
  mode?: 'mireds' | 'kelvin';
  onCommit: (value: number) => void;
}

export function ColorTempSlider({ value, min = 153, max = 500, mode = 'mireds', onCommit }: ColorTempProps) {
  const isKelvin = mode === 'kelvin';
  const kelvinLabel = isKelvin ? Math.round(value) : Math.round(1000000 / value);
  const leftLabel = isKelvin ? 'Warm' : 'Cool';
  const rightLabel = isKelvin ? 'Cool' : 'Warm';
  const gradient = isKelvin
    ? 'linear-gradient(to right, #ffaa44 0%, #fff5e0 50%, #cce0ff 100%)'
    : 'linear-gradient(to right, #cce0ff 0%, #fff5e0 50%, #ffaa44 100%)';
  return (
    <div>
      <div style={{
        fontSize: 11, color: 'var(--text-muted)', marginBottom: 6,
        display: 'flex', justifyContent: 'space-between',
      }}>
        <span>{leftLabel}</span>
        <span>{kelvinLabel}K</span>
        <span>{rightLabel}</span>
      </div>
      <input
        type="range"
        min={min}
        max={max}
        value={value}
        style={{
          width: '100%',
          height: 8,
          borderRadius: 4,
          appearance: 'none' as const,
          background: gradient,
          cursor: 'pointer',
        }}
        onChange={e => onCommit(Number(e.target.value))}
      />
    </div>
  );
}

function hslToRgb(h: number, s: number, l: number): [number, number, number] {
  let r: number, g: number, b: number;
  if (s === 0) {
    r = g = b = l;
  } else {
    const hue2rgb = (p: number, q: number, t: number) => {
      if (t < 0) t += 1;
      if (t > 1) t -= 1;
      if (t < 1 / 6) return p + (q - p) * 6 * t;
      if (t < 1 / 2) return q;
      if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6;
      return p;
    };
    const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
    const p = 2 * l - q;
    r = hue2rgb(p, q, h + 1 / 3);
    g = hue2rgb(p, q, h);
    b = hue2rgb(p, q, h - 1 / 3);
  }
  return [Math.round(r * 255), Math.round(g * 255), Math.round(b * 255)];
}
