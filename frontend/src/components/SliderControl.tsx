import { useState, useRef } from 'react';

interface Props {
  value: number;
  min?: number;
  max?: number;
  className?: string;
  accentColor: string;
  trackColor?: string;
  onCommit: (value: number) => void;
}

export function SliderControl({ value, min = 0, max = 100, className, accentColor, trackColor = 'var(--bg-hover)', onCommit }: Props) {
  const [localValue, setLocalValue] = useState(value);
  const isDragging = useRef(false);
  const pendingValue = useRef(value);

  const displayValue = isDragging.current ? localValue : value;
  const pct = ((displayValue - min) / (max - min)) * 100;

  return (
    <input
      type="range"
      className={className}
      min={min}
      max={max}
      value={displayValue}
      style={{
        width: '100%',
        background: `linear-gradient(to right, ${accentColor} ${pct}%, ${trackColor} ${pct}%)`,
      }}
      onPointerDown={() => {
        isDragging.current = true;
      }}
      onChange={(e) => {
        const v = Number(e.target.value);
        pendingValue.current = v;
        setLocalValue(v);
      }}
      onPointerUp={() => {
        if (isDragging.current) {
          isDragging.current = false;
          onCommit(pendingValue.current);
        }
      }}
    />
  );
}
