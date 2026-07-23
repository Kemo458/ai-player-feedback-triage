import { useState } from 'react';
import { usePipeline } from '../lib/queries';

function fmtEta(sec: number): string {
  if (sec <= 0) return '—';
  if (sec < 60) return '<1m';
  if (sec < 3600) return `${Math.round(sec / 60)}m`;
  const h = Math.floor(sec / 3600);
  const m = Math.round((sec % 3600) / 60);
  return m > 0 ? `${h}h ${m}m` : `${h}h`;
}

function Stage({
  label,
  num,
  sub,
  className = '',
  active = false,
  hot = false,
  led = false,
  color,
}: {
  label: string;
  num: number;
  sub?: string;
  className?: string;
  active?: boolean;
  hot?: boolean;
  led?: boolean;
  color?: string;
}) {
  return (
    <div className={`pipe-stage ${className} ${active ? 'active' : ''} ${hot ? 'hot' : ''}`}>
      <div className="st-label">
        {led && <span className="st-led" />}
        {label}
      </div>
      <div className="st-num" style={color ? { color } : undefined}>
        {num.toLocaleString()}
      </div>
      {sub && <div className="st-sub">{sub}</div>}
    </div>
  );
}

export function PipelineFlow({ gameId }: { gameId: string }) {
  const { data: d } = usePipeline(gameId);
  const [expanded, setExpanded] = useState(false);

  if (!d) {
    return (
      <div className="pipeline reveal">
        <div className="pipe-head">
          <div className="pipe-title">
            <span className="pulse-dot" /> Analysis pipeline
          </div>
        </div>
        <div className="pipe-flow">
          <div className="pipe-stage"><div className="st-label">Imported</div><div className="st-num skeleton" style={{ width: 40, height: 30 }} /></div>
          <div className="pipe-conn" />
          <div className="pipe-stage"><div className="st-label">Queued</div><div className="st-num skeleton" style={{ width: 40, height: 30 }} /></div>
          <div className="pipe-conn" />
          <div className="pipe-stage"><div className="st-label">Analyzing</div><div className="st-num skeleton" style={{ width: 40, height: 30 }} /></div>
          <div className="pipe-conn" />
          <div className="pipe-stage"><div className="st-label">Done</div><div className="st-num skeleton" style={{ width: 40, height: 30 }} /></div>
        </div>
      </div>
    );
  }

  const remaining = d.queued + d.analyzing;
  const pct = d.imported > 0 ? Math.round((d.done / d.imported) * 100) : 0;
  const flowing = d.analyzing > 0;
  const idle = remaining === 0 && d.failed === 0;

  if (idle && !expanded) {
    return (
      <div className="pipeline pipeline-compact reveal">
        <div className="pipeline-compact-main">
          <span className="idle-dot" />
          <div>
            <div className="pipe-title">Analysis pipeline · idle</div>
            <div className="pipeline-compact-copy">
              {d.done.toLocaleString()}/{d.imported.toLocaleString()} analyzed · {pct}% complete
            </div>
          </div>
        </div>
        <div className="pipe-readout">
          <span className="r">
            <b>{d.throughputPerMin}</b>/min observed
          </span>
          <span className="r">{d.concurrency}× workers</span>
          <button className="btn sm ghost" onClick={() => setExpanded(true)}>
            View pipeline
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="pipeline reveal">
      <div className="pipe-head">
        <div className="pipe-title">
          <span className={flowing ? 'pulse-dot' : 'idle-dot'} /> Analysis pipeline
        </div>
        <div className="pipe-readout">
          <span className="r">
            <b>{d.throughputPerMin}</b>/min
          </span>
          <span className="r">{d.concurrency}× workers</span>
          {remaining > 0 && (
            <span className="r amb">
              ~<b>{fmtEta(d.etaSeconds)}</b> left
            </span>
          )}
          {idle && (
            <button className="btn sm ghost" onClick={() => setExpanded(false)}>
              Collapse
            </button>
          )}
        </div>
      </div>

      <div className="pipe-flow">
        <Stage label="Imported" num={d.imported} sub="captured" />
        <div className={`pipe-conn ${d.queued > 0 ? 'flowing' : ''}`} />
        <Stage
          className="stage-queue"
          hot={d.queued > 0}
          label="Queued"
          num={d.queued}
          sub={d.globalQueued > d.queued ? `${d.globalQueued} global` : 'waiting'}
        />
        <div className={`pipe-conn ${flowing ? 'flowing' : ''}`} />
        <Stage
          label="Analyzing"
          num={d.analyzing}
          active={d.analyzing > 0}
          led
          sub={`~${d.avgItemSeconds}s each`}
        />
        <div className={`pipe-conn ${flowing ? 'flowing' : ''}`} />
        <Stage
          label="Done"
          num={d.done}
          sub={d.failed > 0 ? `${d.failed} failed` : 'analyzed'}
          color="var(--green)"
        />
      </div>

      <div className="pipe-progress">
        <span className="done-of">
          {d.done}/{d.imported}
        </span>
        <div className="bar">
          <div className="fill" style={{ width: `${pct}%` }} />
        </div>
        <span className="pct">{pct}%</span>
      </div>
    </div>
  );
}
