import { useState } from 'react';
import type { FilterSection, ActiveFilters } from '../types';

interface FilterPanelProps {
  sections: FilterSection[];
  activeFilters: ActiveFilters;
  onFilterChange: (filters: ActiveFilters) => void;
  search?: string;
  onSearchChange?: (value: string) => void;
  searchPlaceholder?: string;
}

function FilterSectionGroup({
  section,
  active,
  onToggle,
}: {
  section: FilterSection;
  active: Set<string>;
  onToggle: (value: string) => void;
}) {
  const [collapsed, setCollapsed] = useState(false);

  return (
    <div className="filter-section">
      <div className="filter-section-header" onClick={() => setCollapsed(!collapsed)}>
        <span className="filter-section-label">{section.label}</span>
        <span className={`filter-section-chevron${collapsed ? ' collapsed' : ''}`}>&#9662;</span>
      </div>
      {!collapsed && section.options.map(opt => (
        <div
          key={opt.value}
          className="filter-option"
          onClick={() => onToggle(opt.value)}
        >
          <div className={`filter-option-checkbox${active.has(opt.value) ? ' checked' : ''}`}>
            {active.has(opt.value) && (
              <svg width="10" height="8" viewBox="0 0 10 8" fill="none">
                <path d="M1 3.5L3.5 6L9 1" stroke="#0A0B0E" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"/>
              </svg>
            )}
          </div>
          <span className="filter-option-label">{opt.label}</span>
          <span className="filter-option-count">({opt.count})</span>
        </div>
      ))}
    </div>
  );
}

export function FilterPanel({ sections, activeFilters, onFilterChange, search, onSearchChange, searchPlaceholder }: FilterPanelProps) {
  const hasActiveFilters = Object.values(activeFilters).some(s => s.size > 0);

  function handleToggle(sectionKey: string, value: string) {
    const next = { ...activeFilters };
    const set = new Set(next[sectionKey] ?? []);
    if (set.has(value)) {
      set.delete(value);
    } else {
      set.add(value);
    }
    next[sectionKey] = set;
    onFilterChange(next);
  }

  function handleClear() {
    const cleared: ActiveFilters = {};
    for (const key of Object.keys(activeFilters)) {
      cleared[key] = new Set();
    }
    onFilterChange(cleared);
    onSearchChange?.('');
  }

  return (
    <div className="filter-panel">
      <div className="filter-panel-header">
        <span className="filter-panel-title">Filters</span>
        {hasActiveFilters && (
          <button className="filter-clear-btn" onClick={handleClear}>Clear all</button>
        )}
      </div>
      {onSearchChange && (
        <input
          className="filter-search"
          type="text"
          placeholder={searchPlaceholder ?? 'Search...'}
          value={search ?? ''}
          onChange={e => onSearchChange(e.target.value)}
        />
      )}
      {sections.map(section => (
        <FilterSectionGroup
          key={section.key}
          section={section}
          active={activeFilters[section.key] ?? new Set()}
          onToggle={value => handleToggle(section.key, value)}
        />
      ))}
    </div>
  );
}

export function MobileFilterDrawer({
  open,
  onClose,
  children,
}: {
  open: boolean;
  onClose: () => void;
  children: React.ReactNode;
}) {
  return (
    <>
      <div className={`filter-drawer-backdrop${open ? ' open' : ''}`} onClick={onClose} />
      <div className={`filter-drawer${open ? ' open' : ''}`}>
        <div style={{ padding: '16px', borderBottom: '1px solid var(--border-subtle)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <span style={{ fontFamily: 'var(--font-heading)', fontSize: 15, fontWeight: 600, color: 'var(--text-primary)' }}>Filters</span>
          <button onClick={onClose} style={{ fontSize: 18, color: 'var(--text-muted)', padding: '4px 8px' }}>&times;</button>
        </div>
        {children}
      </div>
    </>
  );
}
