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
  const [dragging, setDragging] = useState(false);
  const [localValue, setLocalValue] = useState(value);
  const commitRef = useRef(onCommit);
  commitRef.current = onCommit;

  const displayValue = dragging ? localValue : value;
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
      onChange={(e) => {
        setDragging(true);
        setLocalValue(Number(e.target.value));
      }}
      onMouseUp={() => {
        if (dragging) {
          commitRef.current(localValue);
          setDragging(false);
        }
      }}
      onTouchEnd={() => {
        if (dragging) {
          commitRef.current(localValue);
          setDragging(false);
        }
      }}
    />
  );
}
