(() => {
  "use strict";
  const $ = (id) => document.getElementById(id);
  const state = { runs: [], limit: 250 };
  const fmt = new Intl.NumberFormat("en", {
    notation: "compact",
    maximumFractionDigits: 1,
  });
  const date = (value) =>
    value
      ? new Intl.DateTimeFormat("en", {
          dateStyle: "medium",
          timeStyle: "short",
        }).format(new Date(value))
      : "Unknown";
  const duration = (seconds) =>
    seconds == null
      ? "Unknown"
      : seconds < 60
        ? `${seconds}s`
        : `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
  const label = (run) =>
    run.status === "completed" ? run.conclusion || "unknown" : run.status;
  const text = (id, value) => {
    $(id).textContent = value;
  };

  function renderOutcomes(runs) {
    const counts = {};
    runs.forEach((run) => {
      const key = label(run);
      counts[key] = (counts[key] || 0) + 1;
    });
    const entries = Object.entries(counts).sort((a, b) => b[1] - a[1]);
    const max = Math.max(1, ...entries.map((v) => v[1]));
    const chart = $("outcome-chart");
    const values = $("outcome-text");
    chart.replaceChildren();
    values.replaceChildren();
    entries.forEach(([name, count]) => {
      const bar = document.createElement("div");
      bar.className = `outcome-bar ${name}`;
      bar.style.height = `${Math.max(3, (count / max) * 100)}%`;
      const number = document.createElement("span");
      number.textContent = fmt.format(count);
      bar.append(number);
      chart.append(bar);
      const li = document.createElement("li");
      li.textContent = `${name.replaceAll("_", " ")} ${count}`;
      values.append(li);
    });
    chart.setAttribute(
      "aria-label",
      entries.length
        ? entries.map(([k, v]) => `${k}: ${v}`).join(", ")
        : "No runs in the sample",
    );
    return counts;
  }

  function renderTrend(runs) {
    const svg = $("trend-chart");
    [...svg.querySelectorAll("path,line,circle,text")].forEach((node) =>
      node.remove(),
    );
    const valid = runs
      .map((r) => new Date(r.createdAt))
      .filter((d) => !Number.isNaN(d.valueOf()));
    const min = valid.length ? Math.min(...valid) : 0,
      max = valid.length ? Math.max(...valid) : 0;
    const bins =
      valid.length > 1 && max > min
        ? Array.from({ length: 18 }, (_, i) => ({
            start: min + ((max - min) * i) / 18,
            count: 0,
          }))
        : [];
    valid.forEach((d) => {
      if (bins.length)
        bins[Math.min(17, Math.floor(((d - min) / (max - min)) * 18))].count++;
    });
    const desc = $("trend-desc"),
      list = $("trend-text");
    list.replaceChildren();
    if (!bins.length) {
      desc.textContent =
        "No time trend is available until the sample contains runs at distinct timestamps.";
      return;
    }
    const peak = Math.max(...bins.map((b) => b.count), 1);
    const points = bins.map((b, i) => [
      20 + i * (680 / 17),
      190 - (b.count / peak) * 160,
    ]);
    const ns = "http://www.w3.org/2000/svg";
    for (let i = 0; i < 4; i++) {
      const line = document.createElementNS(ns, "line");
      line.setAttribute("x1", "20");
      line.setAttribute("x2", "700");
      line.setAttribute("y1", String(30 + i * 53));
      line.setAttribute("y2", String(30 + i * 53));
      line.setAttribute("stroke", "currentColor");
      line.setAttribute("opacity", ".12");
      svg.append(line);
    }
    const path = document.createElementNS(ns, "path");
    path.setAttribute(
      "d",
      points.map((p, i) => `${i ? "L" : "M"}${p[0]},${p[1]}`).join(" "),
    );
    path.setAttribute("fill", "none");
    path.setAttribute("stroke", "var(--acid)");
    path.setAttribute("stroke-width", "4");
    path.setAttribute("vector-effect", "non-scaling-stroke");
    svg.append(path);
    points.forEach((p) => {
      const c = document.createElementNS(ns, "circle");
      c.setAttribute("cx", String(p[0]));
      c.setAttribute("cy", String(p[1]));
      c.setAttribute("r", "3");
      c.setAttribute("fill", "var(--ink)");
      svg.append(c);
    });
    [
      [20, new Date(min).toISOString().slice(11, 16), "start"],
      [700, new Date(max).toISOString().slice(11, 16), "end"],
    ].forEach(([x, value, anchor]) => {
      const axis = document.createElementNS(ns, "text");
      axis.setAttribute("x", String(x));
      axis.setAttribute("y", "214");
      axis.setAttribute("text-anchor", anchor);
      axis.setAttribute("fill", "currentColor");
      axis.setAttribute("opacity", ".6");
      axis.setAttribute("font-size", "12");
      axis.textContent = value;
      svg.append(axis);
    });
    bins.forEach((b, i) => {
      const li = document.createElement("li");
      li.textContent = `Bin ${i + 1}: ${b.count} runs`;
      list.append(li);
    });
    desc.textContent = `${runs.length} runs grouped into 18 equal intervals from ${date(new Date(min).toISOString())} through ${date(new Date(max).toISOString())}. Values: ${bins.map((b) => b.count).join(", ")}.`;
  }

  function renderWorkflowHealth(runs) {
    const grouped = new Map();
    runs.forEach((r) => {
      const group = grouped.get(r.workflow) || [];
      group.push(r);
      grouped.set(r.workflow, group);
    });
    const rows = [...grouped]
      .map(([name, rs]) => ({
        name,
        runs: rs.length,
        success: rs.filter((r) => r.conclusion === "success").length,
        fail: rs.filter((r) => r.conclusion === "failure").length,
      }))
      .sort((a, b) => b.runs - a.runs)
      .slice(0, 12);
    const root = $("workflow-list");
    root.replaceChildren();
    rows.forEach((row) => {
      const article = document.createElement("article");
      article.className = "workflow";
      const h = document.createElement("h4");
      h.textContent = row.name;
      h.title = row.name;
      const score = document.createElement("strong");
      const rate = Math.round((row.success / row.runs) * 100);
      score.textContent = `${rate}%`;
      const track = document.createElement("div");
      track.className = "workflow-track";
      const fill = document.createElement("span");
      fill.style.width = `${rate}%`;
      track.append(fill);
      const meta = document.createElement("div");
      meta.className = "workflow-meta";
      meta.textContent = `${row.runs} sampled · ${row.success} success · ${row.fail} failure`;
      article.append(h, score, track, meta);
      root.append(article);
    });
  }

  function renderTable() {
    const query = $("search").value.trim().toLowerCase(),
      outcome = $("outcome-filter").value,
      order = $("sort").value;
    let runs = state.runs.filter(
      (r) =>
        (outcome === "all" || label(r) === outcome) &&
        (!query || `${r.workflow} ${r.event}`.toLowerCase().includes(query)),
    );
    runs.sort((a, b) =>
      order === "slowest"
        ? (b.durationSeconds ?? -1) - (a.durationSeconds ?? -1)
        : order === "oldest"
          ? String(a.createdAt).localeCompare(String(b.createdAt))
          : String(b.createdAt).localeCompare(String(a.createdAt)),
    );
    const shown = Math.min(state.limit, runs.length);
    text(
      "result-count",
      `showing ${shown} · ${runs.length} matched · ${state.runs.length} sampled`,
    );
    $("show-more").hidden = shown >= runs.length;
    const body = $("runs");
    body.replaceChildren();
    runs.slice(0, state.limit).forEach((run) => {
      const tr = document.createElement("tr");
      const td = () => document.createElement("td");
      const first = td(),
        link = document.createElement("a");
      link.href = run.url;
      link.target = "_blank";
      link.rel = "noopener";
      link.textContent = run.workflow;
      first.append(link);
      const event = td();
      event.textContent = run.event;
      const result = td(),
        badge = document.createElement("span");
      badge.className = `badge ${label(run)}`;
      badge.textContent = label(run).replaceAll("_", " ");
      result.append(badge);
      const started = td();
      started.textContent = date(run.startedAt || run.createdAt);
      const elapsed = td();
      elapsed.textContent = duration(run.durationSeconds);
      const attempt = td();
      attempt.textContent = String(run.attempt);
      tr.append(first, event, result, started, elapsed, attempt);
      body.append(tr);
    });
  }

  function renderLocal(host) {
    if (host.schema !== "fsgg.telemetry.dashboard-host/1") {
      text(
        "local-state",
        host.status === "unconfigured"
          ? "Host unconfigured"
          : "Host unavailable",
      );
      return;
    }
    const hostAge = Date.now() - Date.parse(host.observedAt);
    text(
      "local-state",
      `${hostAge > 60 * 60 * 1000 ? "Stale · " : ""}Observed ${date(host.observedAt)}`,
    );
    const content = $("local-content");
    content.replaceChildren();
    const grid = document.createElement("div");
    grid.className = "local-grid";
    const measured = host.totals.usageObservations > 0;
    const known = (value) => (measured ? fmt.format(value) : "Unknown");
    const cards = [
      [
        "Input tokens",
        known(host.usage.input),
        "Aggregate input includes cached components; never sum it with cached input.",
      ],
      [
        "Output tokens",
        known(host.usage.output),
        "Reasoning is shown separately when the source provides it.",
      ],
      [
        "Total tokens",
        known(host.usage.total),
        "All-time aggregate; dashboard date filters do not apply.",
      ],
      [
        "Admitted / terminal",
        `${fmt.format(host.launcherPopulation.admitted)} / ${fmt.format(host.launcherPopulation.terminal)}`,
        "Coverage counts from repository-owned launch boundaries.",
      ],
      [
        "Budget breaches",
        fmt.format(host.budget.distinctBreaches),
        "Canonical epoch count; 5% target and more-than-10% ceiling.",
      ],
      [
        "Intervention",
        host.budget.intervention,
        "Required after 15 distinct breaches or any item exceeds 25%; reset only after verified deployment.",
      ],
    ];
    cards.forEach(([title, value, note]) => {
      const article = document.createElement("article");
      article.className = "local-card";
      const h = document.createElement("h3");
      h.textContent = title;
      const strong = document.createElement("strong");
      strong.textContent = value;
      const p = document.createElement("p");
      p.textContent = note;
      article.append(h, strong, p);
      grid.append(article);
    });
    content.append(grid);
    const details = document.createElement("div");
    details.className = "detail-grid";
    const add = (title, rows) => {
      const box = document.createElement("details");
      const summary = document.createElement("summary");
      summary.textContent = title;
      box.append(summary);
      rows.forEach(([key, value]) => {
        const row = document.createElement("p");
        const name = document.createElement("span");
        name.textContent = key;
        const result = document.createElement("strong");
        result.textContent = value;
        row.append(name, result);
        box.append(row);
      });
      details.append(box);
    };
    const flatten = (object) =>
      Object.entries(object || {}).map(([key, value]) => [
        key,
        typeof value === "object"
          ? Object.entries(value)
              .map(([k, v]) => `${k} ${v}`)
              .join(" · ")
          : String(value),
      ]);
    add("Token details", [
      [
        "cached input",
        measured ? fmt.format(host.usage.cachedInput) : "unknown",
      ],
      [
        "cache-write input",
        measured ? fmt.format(host.usage.cacheWriteInput) : "unknown",
      ],
      [
        "reasoning",
        measured && host.usage.reasoning != null
          ? fmt.format(host.usage.reasoning)
          : "unknown",
      ],
      ["observations", String(host.totals.usageObservations)],
    ]);
    add("Evidence quality", flatten(host.quality));
    add(
      "Launcher population and gaps",
      Object.entries(host.launcherPopulation).map(([key, value]) => [
        key,
        String(value),
      ]),
    );
    add("Operational coverage", [
      ["expected dispatches", String(host.operational.expected)],
      ...flatten(host.operational.lineage),
      ...flatten(host.operational.timing),
      ["usage coverage", "not evaluated"],
      ["terminal outcome coverage", "not evaluated"],
    ]);
    add("Locally attributed CI", [
      [
        "runs / jobs",
        `${host.localCi.counts.runs} / ${host.localCi.counts.jobs}`,
      ],
      ...Object.entries(host.localCi.seconds).map(([key, value]) => [
        key,
        `${duration(value.totalItemSeconds)} across ${value.knownItems} known item(s); ${value.unknownItems} unknown`,
      ]),
      ...flatten(host.localCi.coverage),
    ]);
    add("Canonical budget assessments", [
      ["dirty items", String(host.budget.dirtyItems)],
      [
        "health",
        flatten(host.budget.health)
          .map((row) => row.join(" "))
          .join(" · ") || "unknown",
      ],
      [
        "dimension counts",
        flatten(host.budget.dimensions)
          .map((row) => row.join(" "))
          .join(" · ") || "unknown",
      ],
      ...(host.budget.assessments.length
        ? host.budget.assessments.map((value) => [
            value.dimension,
            `${value.verdict} · ${value.numerator ?? "unknown"} / ${value.denominator ?? "unknown"}${value.severe ? " · severe" : ""}`,
          ])
        : [["assessments", "unknown"]]),
    ]);
    add("Store health", [
      ["status", host.store.status],
      [
        "schema / journal",
        `${host.store.schemaVersion} / ${host.store.journalMode}`,
      ],
      ["pending batches", String(host.store.pendingBatches)],
    ]);
    content.append(details);
  }

  function validate(data) {
    if (
      !data ||
      data.schema !== "fsgg.telemetry.dashboard/1" ||
      !data.actions ||
      data.actions.schema !== "fsgg.telemetry.public-actions/1" ||
      !Array.isArray(data.actions.runs) ||
      !data.actions.selection
    )
      throw new Error(
        "The published data does not match the dashboard contract.",
      );
    if (
      typeof data.actions.observedAt !== "string" ||
      Number.isNaN(Date.parse(data.actions.observedAt))
    )
      throw new Error("The public Actions source has not been observed yet.");
    data.actions.runs.forEach((r) => {
      if (
        !r ||
        typeof r.workflow !== "string" ||
        typeof r.url !== "string" ||
        !r.url.startsWith("https://github.com/FS-GG/.github/actions/runs/")
      )
        throw new Error("A run record is malformed.");
    });
    return data;
  }

  async function start() {
    try {
      const response = await fetch("data/dashboard.json", {
        cache: "no-store",
      });
      if (!response.ok)
        throw new Error(`Data request failed (${response.status}).`);
      const data = validate(await response.json());
      state.runs = data.actions.runs;
      const counts = renderOutcomes(state.runs);
      renderTrend(state.runs);
      renderWorkflowHealth(state.runs);
      renderLocal(data.host);
      renderTable();
      const completed = state.runs
        .filter((r) => r.durationSeconds != null)
        .map((r) => r.durationSeconds)
        .sort((a, b) => a - b);
      const middle = Math.floor(completed.length / 2);
      const median = completed.length
        ? completed.length % 2
          ? completed[middle]
          : Math.round((completed[middle - 1] + completed[middle]) / 2)
        : null;
      text("m-runs", fmt.format(state.runs.length));
      text("m-success", fmt.format(counts.success || 0));
      text("m-duration", duration(median));
      text(
        "m-workflows",
        fmt.format(new Set(state.runs.map((r) => r.workflow)).size),
      );
      const refreshHealth = () => {
        const age = Date.now() - Date.parse(data.actions.observedAt);
        text(
          "health-label",
          state.runs.length
            ? age > 30 * 60 * 1000
              ? "Public Actions stale"
              : "Public Actions live"
            : "Public Actions sample empty",
        );
        $("pulse-dot").classList.toggle(
          "live",
          state.runs.length > 0 && age <= 30 * 60 * 1000,
        );
      };
      refreshHealth();
      setInterval(refreshHealth, 60000);
      text("observed-at", date(data.actions.observedAt));
      const s = data.actions.selection;
      text(
        "sample-note",
        `${s.returned} newest runs${s.truncated ? ` of ${fmt.format(s.repositoryTotalAtObservation)} at observation` : ""}. Coverage: ${date(s.oldestCreatedAt)} → ${date(s.newestCreatedAt)}. ${s.pagesFetched} API page${s.pagesFetched === 1 ? "" : "s"}.`,
      );
      text(
        "provenance",
        `Source revision ${data.sourceRevision} · data build ${date(data.builtAt)} · Actions observation ${date(data.actions.observedAt)}${data.host.observedAt ? ` · host observation ${date(data.host.observedAt)} · host data revision ${data.hostRevision}` : ""}`,
      );
      const outcomes = [...new Set(state.runs.map(label))].sort();
      const select = $("outcome-filter");
      outcomes.forEach((value) => {
        const option = document.createElement("option");
        option.value = value;
        option.textContent = value.replaceAll("_", " ");
        select.append(option);
      });
    } catch (error) {
      $("error").hidden = false;
      text("error", `Signal unavailable: ${error.message}`);
      text("health-label", "Data unavailable");
    }
  }
  ["search", "outcome-filter", "sort"].forEach((id) =>
    $(id).addEventListener(id === "search" ? "input" : "change", () => {
      state.limit = 250;
      renderTable();
    }),
  );
  $("show-more").addEventListener("click", () => {
    state.limit += 250;
    renderTable();
  });
  $("theme").addEventListener("click", () => {
    const html = document.documentElement;
    html.dataset.theme = html.dataset.theme === "dark" ? "light" : "dark";
    localStorage.setItem("signal-theme", html.dataset.theme);
  });
  const saved = localStorage.getItem("signal-theme");
  if (saved === "light" || saved === "dark")
    document.documentElement.dataset.theme = saved;
  start();
})();
