(() => {
  "use strict";
  const $ = (id) => document.getElementById(id);
  const state = { runs: [], items: [], deliveries: [], deliveryFeed: null, itemStatus: "Item details await a configured host", limit: 250 };
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

  const itemTime = (item) =>
    item.runtime.duration.rows.reduce((sum, row) => sum + row.summedSeconds, 0);
  const itemTokens = (item) => {
    const total=item.runtime.tokens.total;
    return total?.status==="complete" ? total.total : null;
  };
  function metricTable(headings, rows, labelText) {
    const wrap = document.createElement("div");
    wrap.className = "table-wrap compact-table";
    wrap.tabIndex = 0;
    wrap.setAttribute("aria-label", labelText);
    const table = document.createElement("table"), thead = document.createElement("thead"), tr = document.createElement("tr"), body = document.createElement("tbody");
    headings.forEach((heading) => { const th=document.createElement("th"); th.textContent=heading; tr.append(th); });
    thead.append(tr);
    rows.forEach((values) => { const row=document.createElement("tr"); values.forEach((value) => { const td=document.createElement("td"); td.textContent=value; row.append(td); }); body.append(row); });
    table.append(thead,body); wrap.append(table); return wrap;
  }
  function recordCards(rows, labelText) {
    const root=document.createElement("div"); root.className="record-cards"; root.setAttribute("aria-label",labelText);
    rows.forEach((fields)=>{const article=document.createElement("article");fields.forEach(([name,value])=>{const p=document.createElement("p"),label=document.createElement("span"),content=document.createElement("strong");label.textContent=name;content.textContent=value;p.append(label,content);article.append(p);});root.append(article);});
    return root;
  }
  function tokenBreakdown(rows) {
    const root=document.createElement("div"); root.className="token-rows"; root.setAttribute("aria-label","Token usage by approved requested and observed category and scope");
    rows.forEach((row)=>{const article=document.createElement("article"),h=document.createElement("h5"),scope=document.createElement("small"),metrics=document.createElement("p");h.textContent=`${row.role} · ${row.requestedModel} (${row.requestedEffort}) → ${row.observedModel} (${row.observedEffort})`;scope.textContent=row.scope;metrics.textContent=`Input ${fmt.format(row.input)} · cached ${fmt.format(row.cachedInput)} · output ${fmt.format(row.output)} · reasoning ${row.reasoning==null?"unknown":fmt.format(row.reasoning)} · total ${fmt.format(row.total)}`;article.append(h,scope,metrics);root.append(article);});
    if (!rows.length) { const p=document.createElement("p"); p.textContent="No completed-turn token observation is available."; root.append(p); }
    return root;
  }
  const friendly = (value) => String(value).replaceAll("-", " ").replace(/\b\w/g,(letter)=>letter.toUpperCase());
  function processDetail(process, usageCoverage) {
    const root=document.createElement("div"); root.className="process-detail";
    if (!process || process.availability!=="available") {
      const p=document.createElement("p"); p.className="gap-note"; p.textContent="Activity, typed complication, and process-review telemetry is unavailable in this host snapshot."; root.append(p); return root;
    }
    const coverage=document.createElement("p"); coverage.className="item-method";
    const truncated=Object.entries(process.truncated).filter(([,flag])=>flag).map(([name])=>name);
    coverage.textContent=`Engine detail available for ${process.members.available} member item(s). These records and the completion projection share one engine-owned database snapshot.${truncated.length?` Truncated: ${truncated.join(", ")}; shown detail is incomplete.`:""}`;
    root.append(coverage);
    const grid=document.createElement("div"); grid.className="process-grid";
    const block=(heading,node)=>{const section=document.createElement("section"),h=document.createElement("h5");h.textContent=heading;section.append(h,node);grid.append(section);};
    const activity=document.createElement("div"), peak=Math.max(1,...process.activities.summary.map((row)=>row.summedSeconds));
    const bars=document.createElement("div"); bars.className="time-bars activity-bars";
    process.activities.summary.forEach((entry)=>{const row=document.createElement("div"),name=document.createElement("span"),track=document.createElement("i"),fill=document.createElement("b");name.textContent=`${friendly(entry.category)} · ${entry.knownDuration?duration(entry.summedSeconds):"Unknown"}${entry.open?` · ${entry.open} open`:""}`;fill.style.width=`${entry.knownDuration?(entry.summedSeconds/peak)*100:0}%`;track.append(fill);row.append(name,track);bars.append(row);});
    if (!process.activities.summary.length) { const p=document.createElement("p"); p.textContent="No activity span was returned."; activity.append(p); }
    activity.append(bars,metricTable(["Category","Spans / open","Known summed time"],process.activities.summary.map((row)=>[friendly(row.category),`${row.spans} / ${row.open}`,row.knownDuration?(row.open?`${duration(row.summedSeconds)} known portion`:duration(row.summedSeconds)):"Unknown"]),"Activity span time by category; spans may overlap"));
    if (process.activities.rows.length) activity.append(recordCards(process.activities.rows.map((row)=>[["Activity",friendly(row.category)],["Started",date(row.startedAt)],["Ended",row.endedAt?date(row.endedAt):"Open"],["Observed span",row.durationSeconds==null?"Unknown":duration(row.durationSeconds)]]),"Recorded activity spans in observed order"));
    block("Activity time and spans",activity);
    const attributed=process.attribution.rows.length?metricTable(["Class / activity","Records","Input / cached","Output / reasoning","Total"],process.attribution.rows.map((row)=>[`${friendly(row.classification)}${row.activityCategory?` · ${friendly(row.activityCategory)}`:""}`,String(row.records),`${fmt.format(row.input)} / ${fmt.format(row.cachedInput)}`,`${fmt.format(row.output)} / ${row.reasoning==null?"unknown":fmt.format(row.reasoning)}`,fmt.format(row.total)]),"Direct, mixed and unclassified activity token attribution"):document.createElement("p");
    if (process.attribution.rows.length) attributed.classList.add("wide-table"); else attributed.textContent="No attributed native usage records were returned.";
    const accounting=process.attribution.accounting, noNative=usageCoverage&&usageCoverage.invocationsWithUsage===0, accountNote=document.createElement("p"); accountNote.className="gap-note"; accountNote.textContent=`Diagnostic cross-scope accounting: native ${fmt.format(accounting.nativeTotal)} · direct ${fmt.format(accounting.direct)} · mixed ${fmt.format(accounting.mixed)} · unclassified ${fmt.format(accounting.unclassified)} · ${accounting.missingAttribution} native usage row(s) missing attribution. Native and attributed totals are related views, not additive.${noNative?" No native usage was observed; zero does not establish zero cost or complete capture.":""}${process.attribution.crossRead==="partial"?" Independent reads did not match; coverage is partial.":""}`;
    const attributionBox=document.createElement("div"); attributionBox.append(accountNote,attributed); block("Activity-attributed tokens",attributionBox);
    const complications=process.complications.rows.length?recordCards(process.complications.rows.map((row)=>[["Trigger",friendly(row.trigger)],["Recorded cause",friendly(row.cause)],["Activity",row.activityCategory?friendly(row.activityCategory):"Unlinked"],["Observed",date(row.occurredAt)]]),"Typed recorded complications; private synopsis omitted"):document.createElement("p");
    if (!process.complications.rows.length) complications.textContent="No typed complication event was returned."; block("Recorded complications",complications);
    const countText=(counts)=>`well ${counts.wentWell} · problems ${counts.problems} · delay/rework ${counts.avoidableDelayOrRework} · observations ${counts.processObservations} · risks ${counts.remainingRisks} · improvements ${counts.concreteImprovements}`;
    const reviews=process.reviews.rows.length?recordCards(process.reviews.rows.map((row)=>[["Scope / revision",`${friendly(row.scope)} · r${row.revision}`],["Confidence",row.confidence],["Declared coverage",`evidence ${row.evidenceCoverage} · population ${row.populationCoverage}`],["Reviewer",`${row.reviewerModel} · ${row.reviewerEffort}`],["Reviewed / duration",`${date(row.reviewedAt)} · ${duration(row.durationSeconds)}`],["Private-list counts",countText(row.counts)]]),"Process review metadata; private findings omitted"):document.createElement("p");
    if (!process.reviews.rows.length) reviews.textContent="No process review was returned. An attempt review is distinct from an item review."; const reviewBox=document.createElement("div"),reviewNote=document.createElement("p");reviewNote.className="gap-note";reviewNote.textContent="Review prose stays private. High confidence does not prove item completeness or token coverage; review duration is not added to activity or runtime time.";reviewBox.append(reviewNote,reviews);block("Process reviews",reviewBox);
    root.append(grid); return root;
  }
  function renderItems() {
    const query=$("item-search").value.trim().toLowerCase(), order=$("item-sort").value;
    const rows=state.items.filter((item)=>!query || `${item.label} ${item.deliveries.map((d)=>d.repository+" "+d.number).join(" ")}`.toLowerCase().includes(query));
    rows.sort((a,b)=>order==="time"?itemTime(b)-itemTime(a):order==="tokens"?(itemTokens(b)??-1)-(itemTokens(a)??-1):String(b.deliveredAt).localeCompare(String(a.deliveredAt)));
    const root=$("completed-items"); root.replaceChildren();
    if (!rows.length) {
      const empty=document.createElement("div"); empty.className="empty-state item-empty";
      const h=document.createElement("h3"); h.textContent=state.items.length?"No matching completed item":state.itemStatus;
      const p=document.createElement("p"); p.textContent="Merged deliveries below remain independently available. Only current canonical completed population with delivered native outcomes appears here.";
      empty.append(h,p); root.append(empty); return;
    }
    rows.forEach((item)=>{
      const details=document.createElement("details"); details.className="item-card"; details.id=`item-${item.key}`;
      if (location.hash===`#item-${item.key}`) { details.open=true; requestAnimationFrame(()=>details.scrollIntoView({block:"start"})); }
      details.addEventListener("toggle",()=>{if(details.open)history.replaceState(null,"",`#item-${item.key}`);});
      const summary=document.createElement("summary"), title=document.createElement("span"), name=document.createElement("strong"), meta=document.createElement("small");
      name.textContent=item.label; meta.textContent=`${item.runtime.invocations} invocations · ${item.ci.counts.runs} CI runs · delivery recorded ${date(item.deliveredAt)}`; title.append(name,meta);
      const total=document.createElement("b"), tokenTotal=itemTokens(item), usageCoverage=item.runtime.tokens.coverage, fullRuntimeCoverage=usageCoverage.boundary==="canonical completed member items and all expected runtime dispatches";
      total.textContent=fullRuntimeCoverage
        ? tokenTotal==null
          ? `Token coverage ${usageCoverage.status} · ${usageCoverage.invocationsWithUsage}/${usageCoverage.expectedDispatches} with usage`
          : `${fmt.format(tokenTotal)} complete tokens · ${usageCoverage.invocationsWithUsage}/${usageCoverage.expectedDispatches} with usage`
        : tokenTotal==null?"Observed tokens by compatible scope · legacy coverage":`${fmt.format(tokenTotal)} observed tokens · legacy coverage`;
      summary.append(title,total); details.append(summary);
      const intro=document.createElement("p"), coverageText=fullRuntimeCoverage?`Token coverage ${usageCoverage.status}: ${usageCoverage.invocationsWithUsage}/${usageCoverage.expectedDispatches} expected invocation(s) have usage; ${usageCoverage.linkedInvocations}/${usageCoverage.expectedDispatches} are linked, ${usageCoverage.invocationsWithoutUsage} have no usage, and ${usageCoverage.runtimeGaps} runtime gap(s) are recorded. A complete total is shown only for one compatible accounting basis with no unknown remainder.`:`Legacy token coverage: ${usageCoverage.invocationsWithUsage} invocation(s) observed, ${usageCoverage.invocationsWithoutUsage} without usage, ${usageCoverage.runtimeGaps} runtime gap(s); this older contract is absent or limited to codex-exec and does not identify the full expected runtime population.`; intro.className="item-method"; intro.textContent=`Settled means current canonical population is completed and every grouped native delivery is delivered. Invocation spans may overlap; they are not human effort. CI time is separate. ${coverageText}`; details.append(intro);
      const links=document.createElement("div"); links.className="item-links"; const link=document.createElement("a"); link.href=item.url; link.target="_blank"; link.rel="noopener"; link.textContent="Open approved item evidence ↗"; links.append(link);
      item.deliveries.forEach((delivery)=>{const deliveryLink=document.createElement("a");deliveryLink.href=delivery.url;deliveryLink.target="_blank";deliveryLink.rel="noopener";deliveryLink.textContent=`${delivery.repository}#${delivery.number} ↗`;links.append(deliveryLink);});
      const permalink=document.createElement("a");permalink.href=`#item-${item.key}`;permalink.textContent="Permalink #";links.append(permalink);details.append(links);
      const grid=document.createElement("div"); grid.className="item-detail-grid";
      const section=(heading,node)=>{const box=document.createElement("section");const h=document.createElement("h4");h.textContent=heading;box.append(h,node);grid.append(box);};
      const timeBox=document.createElement("div"), peak=Math.max(1,...item.runtime.duration.rows.map((r)=>r.summedSeconds)), bars=document.createElement("div"); bars.className="time-bars";
      item.runtime.duration.rows.forEach((r)=>{const row=document.createElement("div"),name=document.createElement("span"),track=document.createElement("i"),fill=document.createElement("b");name.textContent=`${r.role} · ${r.known?r.unknown?`${duration(r.summedSeconds)} known portion`:duration(r.summedSeconds):"Unknown"}`;fill.style.width=`${r.known?(r.summedSeconds/peak)*100:0}%`;track.append(fill);row.append(name,track);bars.append(row);});
      timeBox.append(bars,metricTable(["Role","Observed","Unknown","Summed invocation spans"],item.runtime.duration.rows.map((r)=>[r.role,String(r.known),String(r.unknown),r.known?(r.unknown?`${duration(r.summedSeconds)} known portion`:duration(r.summedSeconds)):"Unknown"]),"Invocation time by role")); section("Observed invocation time",timeBox);
      section("Completed-turn tokens",tokenBreakdown(item.runtime.tokens.rows));
      const ciLabels={runnerSeconds:"Runner time",wallSeconds:"Union wall time",queueSeconds:"Queue time",usefulValidationSeconds:"Useful validation",administrativeSeconds:"Administration",necessarySetupSeconds:"Necessary setup",mixedSeconds:"Mixed classification",unclassifiedSeconds:"Unclassified"};
      section("CI time",metricTable(["Metric","Known / unknown","Item-seconds"],Object.entries(item.ci.seconds).map(([key,value])=>[ciLabels[key]||key,`${value.knownItems} / ${value.unknownItems}`,value.knownItems?(value.unknownItems?`${duration(value.totalItemSeconds)} known portion`:duration(value.totalItemSeconds)):"Unknown"]),"CI measurements; categories may overlap"));
      section("Canonical budgets",metricTable(["Dimension","Verdict","Numerator / denominator"],item.budget.assessments.map((a)=>[`${a.dimension} · ${a.epoch}`,`${a.verdict}${a.severe?" · severe":""}`,`${a.numerator??"unknown"} / ${a.denominator??"unknown"}`]),"Reducer-owned budget assessments"));
      section("Activity, attribution & reviews",processDetail(item.process,usageCoverage)); grid.lastElementChild.classList.add("process-span");
      const complication=document.createElement("div"), gap=document.createElement("p"); gap.className="gap-note";
      const process=item.process, repair=process?.availability==="available"?process.activities.summary.find((row)=>row.category==="repair"):null, repairTokens=process?.availability==="available"?process.attribution.rows.filter((row)=>row.classification==="direct"&&row.activityCategory==="repair").reduce((sum,row)=>sum+row.total,0):null;
      gap.textContent=repair?`Recorded repair activity: ${repair.knownDuration?duration(repair.summedSeconds):"duration unknown"}${repair.open?" (open span)":""}; ${usageCoverage.invocationsWithUsage?`${fmt.format(repairTokens)} directly attributed tokens`:`repair tokens unknown because no native usage was observed`}. Activity category is separate from bureaucracy, and complication counts do not allocate all attempt cost to repair.`:"Repair time and repair tokens remain unknown without a recorded repair activity and direct attribution. Failed invocation tokens are not repair tokens."; complication.append(gap);
      const eventLabels={runtimeNonSuccess:"Non-success runtime terminals",failedOrCancelledCiRuns:"Failed / cancelled CI runs",repeatedCiRuns:"Repeated CI attempts",followUpInvocations:"Runtime follow-up invocations"};
      complication.append(metricTable(["Observed evidence","Count"],Object.entries(item.complications.observed).map(([key,value])=>[eventLabels[key]||key,String(value)]),"Observed complication signals"));
      item.complications.notes.forEach((note)=>{const p=document.createElement("p"),a=document.createElement("a");p.className="approved-note";p.append(`${note.kind}: ${note.text} `);a.href=note.evidenceUrl;a.target="_blank";a.rel="noopener";a.textContent="evidence ↗";p.append(a);complication.append(p);}); section("Complications and repairs",complication);
      details.append(grid); root.append(details);
    });
  }
  function renderDeliveries(feed) {
    state.deliveryFeed=feed; const query=$("item-search").value.trim().toLowerCase(), order=$("item-sort").value;
    state.deliveries=feed.deliveries.filter((delivery)=>!query || `${delivery.title} ${delivery.number}`.toLowerCase().includes(query));
    state.deliveries.sort((a,b)=>order==="time"?(b.elapsedSeconds??-1)-(a.elapsedSeconds??-1):String(b.mergedAt).localeCompare(String(a.mergedAt)));
    text("delivery-count",`${state.deliveries.length} shown · ${feed.selection.returned} merged from ${feed.selection.closedScanned} closed PRs scanned`);
    const body=$("deliveries"); body.replaceChildren();
    state.deliveries.forEach((delivery)=>{const tr=document.createElement("tr"), first=document.createElement("td"), link=document.createElement("a");link.href=delivery.url;link.target="_blank";link.rel="noopener";link.textContent=`#${delivery.number} ${delivery.title}`;first.append(link);const merged=document.createElement("td");merged.textContent=date(delivery.mergedAt);const elapsed=document.createElement("td");elapsed.textContent=duration(delivery.elapsedSeconds);tr.append(first,merged,elapsed);body.append(tr);});
  }

  function renderLocal(host) {
    if (!["fsgg.telemetry.dashboard-host/1","fsgg.telemetry.dashboard-host/2","fsgg.telemetry.dashboard-host/3"].includes(host.schema)) {
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
        "Observed tokens",
        known(host.usage.total),
        "All-time observed native usage; coverage gaps can make this a partial total, and dashboard date filters do not apply.",
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
      data.schema !== "fsgg.telemetry.dashboard/2" ||
      !data.actions ||
      data.actions.schema !== "fsgg.telemetry.public-actions/1" ||
      !Array.isArray(data.actions.runs) ||
      !data.actions.selection ||
      !data.deliveries ||
      data.deliveries.schema !== "fsgg.telemetry.public-deliveries/1" ||
      !Array.isArray(data.deliveries.deliveries)
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
      const itemCapable=["fsgg.telemetry.dashboard-host/2","fsgg.telemetry.dashboard-host/3"].includes(data.host.schema);
      state.items = itemCapable ? data.host.completedItems.items : [];
      state.itemStatus = itemCapable ? (data.host.completedItems.coverage.incompatible ? "Some completed-item details were rejected as incompatible" : data.host.completedItems.coverage.unmapped ? "Completed items await approved public labels" : data.host.completedItems.coverage.dirty ? "Item completion is pending canonical reduction" : "No completed item is present in the observed host snapshot") : data.host.schema === "fsgg.telemetry.dashboard-host/1" ? "This older host snapshot has aggregate telemetry only" : "Item details await a configured host";
      renderItems();
      renderDeliveries(data.deliveries);
      const itemCoverage = data.host.completedItems?.coverage;
      text("items-note",itemCoverage ? `${itemCoverage.published} published · ${itemCoverage.unmapped} awaiting an approved label · ${itemCoverage.dirty} pending canonical reduction · ${itemCoverage.incompatible} incompatible.` : "Host item projection unavailable. Showing independent public merged deliveries.");
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
        `Source revision ${data.sourceRevision} · data build ${date(data.builtAt)} · Actions observation ${date(data.actions.observedAt)}${data.host.observedAt ? ` · host observation ${date(data.host.observedAt)} · public payload ${data.host.revision || "legacy"} · host commit ${data.hostRevision}` : ""}`,
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
  ["item-search","item-sort"].forEach((id)=>$(id).addEventListener(id==="item-search"?"input":"change",()=>{renderItems();if(state.deliveryFeed)renderDeliveries(state.deliveryFeed);}));
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
