using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using pdb.podcast.Delta;
using pdb.podcast.Tuning;

namespace pdb.podcast.Auto
{
    interface IBuilderSource
    {
        List<TrackInfoItunes> getSelectedTracks();
        double getTotalSize();
        string getEmpreinte();
    }

    enum builderstate
    {
        none,
        enCours,
        stable
    }
    class AutoBuilder : IComparable<AutoBuilder>
    {
        const char SEP = '\t';
        const string DAT = @"..\Data\";
        string file = "";
        private bool mustUpdate; public static bool MustUpdate = false;
        private double total;
        private double delta;
        private double lastd;
        private double d;
        private double org;
        private double newv;
        private bool canGo;
        //private int lastGoodDelta;
        //private double lastGoodLevel;
        private int lastTotal;
        private int atomicMode;
        private int sameConfiguration;

        private double cmin;
        private double cmax;

        private double dmin;
        private double dmax;
        const double EPSILON = 1e-10;

        //private bool dminAlready;
        //private bool dmaxAlready;

        private string _empreinte = "";
        private double delta0 = 0;

        private Memory mem0;
        private Memory mem1;
        private Memory minMem;
        private Memory maxMem;
        private StringBuilder sb = new StringBuilder();

        // private static DateTime lastModif;
        private pdb.podcast.Tuning.Auto conf;

        private static List<AutoBuilder> list = new List<AutoBuilder>();
        private static int index;
        private IBuilderSource source;
        private static XmlElement xmlRoot;
        private string name = "";

        private builderstate state;
        private int naturalOrder = 1;
        private AutoBuilder(pdb.podcast.Tuning.Auto conf)
        {
            this.conf = conf;
            this.name = conf.name;
            mem0 = new Memory(DAT + "v." + conf.name + ".db.txt");
            mem1 = new Memory(DAT + "r." + conf.name + ".db.txt");
            this.file = DAT + "auto." + conf.name + ".log";
        }

        private static AutoBuilder main;

        public static void build(XmlNode xAuto, IBuilderSource source)
        {
            if (xAuto is XmlElement)
            {
                var conf = new pdb.podcast.Tuning.Auto(xAuto as XmlElement);
                var name = conf.name;
                AutoBuilder builder = list.Find(a => a.name == name);
                if (builder == null)
                {
                    builder = new AutoBuilder(conf);
                    list.Add(builder);
                    builder.naturalOrder = list.Count;
                    index = list.Count - 1;
                    if (main == null)
                        main = builder;
                }

                builder.conf = conf;
                builder.source = source;

            }

        }

        private void makeDelta(Memory mem, string mode, string file)
        {
            if (!File.Exists(file))
                File.WriteAllText(file, "quand\tdiff\tcorr\torg\tnew\tcmin\tcmax\tdmin\tdmax\tatomic\tstate\tAdd\tiAdd\ttSupp\tiSup\t\r\n");

            mem.load();
            var listSe = source.getSelectedTracks();
            if (mem.Loaded)
            {
                var auxDixt = new pdb.util.BgDictString<TrackInfoItunes>(); // BgDictString<string, TrackInfoItunes>();
                foreach (TrackInfoItunes s in listSe)
                {
                    var key = s.Location;
                    auxDixt[key] = s;
                    if (!mem.dict.ContainsKey(key))
                    {
                        var desc = new TrackItemdesc(s.ToString(), s.GetProvider());
                        string line = desc.ToString();
                        mem.ajouts.Add(desc);
                        sb.AppendLine(string.Format("{0} delta {1} ajouté {2}", name, mode, line));
                    }
                }

                foreach (string key in mem.dict.Keys)
                {
                    if (!auxDixt.ContainsKey(key))
                    {
                        var line = mem.dict[key];
                        mem.suppressions.Add(line);
                        sb.AppendLine(string.Format("{0} delta {1} supprimé {2}", name, mode, line));
                    }
                }

                if (mem.ajouts.Count > 0 || mem.suppressions.Count > 0)
                {

                    int imax = Math.Max(mem.ajouts.Count, mem.suppressions.Count);

                    using (TextWriter tw = new StreamWriter(file, true, Encoding.UTF8))
                    {
                        //if (!exist)
                        //    tw.WriteLine("quand\tdiff\tcorr\torg\tnew\tAdd\tiAdd\ttSupp\tiSup");
                        tw.Write(DateTime.Now); tw.Write(SEP);
                        tw.Write((delta).ToString("0.###")); tw.Write(SEP);
                        tw.Write(d.ToString("0.###")); tw.Write(SEP);
                        tw.Write(org.ToString("0.###")); tw.Write(SEP);
                        tw.Write(newv.ToString("0.###")); tw.Write(SEP);
                        tw.Write(cmin.ToString("0.###")); tw.Write(SEP);
                        tw.Write(cmax.ToString("0.###")); tw.Write(SEP);
                        tw.Write(dmin.ToString("0.###")); tw.Write(SEP);
                        tw.Write(dmax.ToString("0.###")); tw.Write(SEP);
                        tw.Write(atomicMode); tw.Write(SEP);
                        tw.Write(state); tw.Write(SEP);
                        for (int i = 0; i < imax; i++)
                        {
                            if (i > 0)
                            {
                                tw.WriteLine();
                                for (int j = 0; j < 11; j++)
                                    tw.Write(SEP);
                            }
                            if (i < mem.ajouts.Count)
                                tw.Write(mem.ajouts[i].track);
                            tw.Write(SEP);
                            if (i < mem.ajouts.Count)
                                tw.Write(mem.ajouts[i].item);
                            tw.Write(SEP);
                            if (i < mem.suppressions.Count)
                                tw.Write(mem.suppressions[i].track);
                            tw.Write(SEP);
                            if (i < mem.suppressions.Count)
                                tw.Write(mem.suppressions[i].item);
                            tw.Write(SEP);

                        }
                        tw.WriteLine();

                    }
                }
            }
            mem.save(listSe);
        }

        private void reset()
        {
            atomicMode = -1;
            cmin = 0;
            cmax = int.MaxValue;
            dmin = int.MaxValue;
            dmax = -dmin;
            //dminAlready = false;
            //dmaxAlready = false;
            minMem = null;
            maxMem = null;
            sameConfiguration = 0;
            _empreinte = "";
            delta0 = 0;

            state = builderstate.none;
        }
        private static void decrementIndex()
        {
            index--;
            if (index < 0)
                index = list.Count - 1;
        }

        public static bool Check(Feeds feeds)
        {
            if (list.Count == 0)
                return true;
            list.Sort();

            FileInfo f = Conf.Instance.ConfFile;

            var doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.Load(f.FullName);
            xmlRoot = doc.DocumentElement.SelectSingleNode("./dir") as XmlElement;
            bool _cango = true;
            MustUpdate = false;
            bool _modifFile = false;

            if (Conf.AutoSequence)
            {
                int i = -1;
                for (i = list.Count - 1; i >= 0; i--)
                {
                    var builder = list[i];
                    main = builder;


                    var cango = builder.check();
                    if (!cango)
                        _cango = false;
                    if (builder.mustUpdate)
                        MustUpdate = true;
                    if (builder.newv != builder.org)
                    {
                        _modifFile = true;
                    }


                    if (!cango || MustUpdate || _modifFile)
                        break;


                }
                if (i > 0)
                    _cango = false;
            }
            else
            {
                // application de la précédente modif
                var builder = list[index];
                if (builder.state > builderstate.none)
                {
                    builder.makeEmpreinteNonSequence();
                    decrementIndex();
                }

                int i = -1;
                for (i = list.Count - 1; i >= 0; i--)
                {
                    builder = list[i];


                    bool cango = false;
                    bool _cont = false;
                    while (true)
                    {
                        if (i == index || _cont)
                        {
                            cango = builder.check();
                            index = i; 
                            main = builder;
                            _cont = false; 
                            break;
                        }
                        else
                            cango = builder.checkWithoutModify();
                        if (builder.state == builderstate.none)
                            _cont = true;
                        else
                            break; 
                    }
                    if (!cango)
                        _cango = false;
                    if (builder.mustUpdate)
                        MustUpdate = true;

                    if (i == index)
                    {
                        if (builder.newv != builder.org)
                        {
                            _modifFile = true;
                        }
                        else
                        {
                            //index--;
                            //if (index < 0)
                            //    index = list.Count - 1;
                        }
                    }

                    if(_modifFile)
                        break;
                }

            }

            if (_modifFile)
            {
                f.CopyTo(f.FullName + ".sov", true);
                doc.Save(f.FullName);
            }

            return _cango;
        }
        private void log(string txt)
        {
            Program.log.log(string.Format("{0} {1}", conf.name, txt));
        }
        private void log(string txt, params object[] args)
        {
            string _txt = string.Format(txt, args);
            log(_txt);

        }

        private void makeEmpreinteNonSequence()
        {
            var newEmpreinte = source.getEmpreinte();
            if (state == builderstate.stable)
            {
                if (newEmpreinte != _empreinte)
                    reset();
            }
            _empreinte = newEmpreinte;
        }


        private bool checkWithoutModify()
        {
            mustUpdate = false;
            canGo = false;
            var newEmpreinte = source.getEmpreinte();
            try
            {

                if (state == builderstate.stable)
                {
                    if (newEmpreinte != _empreinte)
                        reset();
                    else
                    {
                        mustUpdate = false;
                        canGo = true;
                        org = newv;
                        return true;
                    }
                }
                else
                {
                    if (newEmpreinte != _empreinte)
                        reset();
                }

                total = source.getTotalSize();
                double target = conf.target;
                delta = target - total;

                if (delta >= 0)
                {

                    if (delta < conf.write)
                        canGo = true;

                    if (delta < conf.delta)
                    {
                        state = builderstate.stable;
                        _empreinte = newEmpreinte;
                        return true;
                    }

                    mustUpdate = true;
                }

                else if (delta < 0)
                {
                    mustUpdate = true;
                    canGo = false;
                }
            }
            finally
            {
                if (state == builderstate.stable)
                {
                    mustUpdate = false;
                    _empreinte = newEmpreinte;
                    canGo = true;
                }
            }
            return canGo;


        }
        private bool check()
        {
            sb = new StringBuilder();
            sb.AppendLine();
            makeDelta(mem0, "virtuel", DAT + "vhisto." + conf.name + ".txt");

            mustUpdate = false;
            canGo = false;

            var newEmpreinte = source.getEmpreinte();
            if (state == builderstate.none)
                reset();
            else if (state == builderstate.stable)
            {
                if (newEmpreinte != _empreinte)
                    reset();
                else
                {
                    mustUpdate = false;
                    canGo = true;
                    org = newv;
                    return true;
                }
            }

            total = source.getTotalSize();
            double target = conf.target;
            delta = target - total;
            log("delta {0}", delta);
            if (state == builderstate.none)
                delta0 = delta;
            state = builderstate.enCours;
            lastd = d;
            d = 0;

            xml = null;

            lookup(xmlRoot);
            if (xml == null)
                return true;

            string attTarget = conf.type;

            var att = xml.Attributes[attTarget];
            if (att == null)
                return true;

            org = Convert.ToDouble(att.Value);

            newv = org;

            try
            {
                if (delta * delta0 < 0)
                {
                    if (atomicMode < 0)
                    {
                        atomicMode = 0;
                        //if (minMem == null)
                        //    minMem = new Memory(mem0);
                        //if (maxMem == null)
                        //    maxMem = new Memory(mem0);
                        //if (minMem.suppressions.Count == 0)
                        //    minMem.suppressions = new List<TrackItemdesc>(maxMem.ajouts);
                        //if (maxMem.ajouts.Count == 0)
                        //    maxMem.ajouts = new List<TrackItemdesc>(minMem.suppressions);
                    }
                }
                bool cancelSameConf = false;
                bool cancelNb = false;
                if (atomicMode >= 0)
                {
                    atomicMode++;
                    if (Math.Abs(dmin - delta) < EPSILON)
                        sameConfiguration++;
                    else if (Math.Abs(dmax - delta) < EPSILON)
                        sameConfiguration++;
                    else
                        sameConfiguration = 0;
                    if (sameConfiguration > conf.idem)
                        cancelSameConf = true;
                    if (atomicMode > conf.cloop)
                        cancelNb = true;

                }

                if (delta >= 0)
                {
                    if (mem0.suppressions.Count > 0)
                        minMem = new Memory(mem0);
                    if (delta < conf.write)
                        canGo = true;
                    //  lastGoodLevel = org;
                    //if (maxMem != null && minMem != null)
                    //{

                    if (atomicMode > 0)
                    {
                        if (cancelSameConf) //|| (mem0.suppressions.Count == 1 && mem0.ajouts.Count ==0))
                        {
                            log("abandon recherche cartésienne cause idem");
                            state = builderstate.stable;
                            return true;
                        }
                        else if (cancelNb)
                        {
                            log("abandon recherche cartésienne cause cloop");
                            state = builderstate.stable;
                            return true;
                        }

                        //if (Math.Abs(dmin - delta) < 0.01)
                        //    dminAlready = true;
                        //else
                        //    dminAlready = false;
                        if (maxMem != null && minMem != null)
                        {
                            if (minMem.suppressions.Count == maxMem.ajouts.Count)
                            {
                                bool identique = true;
                                for (int i = 0; i < minMem.suppressions.Count; i++)
                                {
                                    if (!minMem.suppressions[i].Equals(maxMem.ajouts[i]))
                                    {
                                        identique = false;
                                        break;

                                    }
                                }

                                if (identique)
                                {
                                    if (minMem.suppressions.Count == 1 || atomicMode > conf.verif)
                                    {
                                        mustUpdate = false;
                                        log("abandon recherche cartésienne cause cycle");
                                        state = builderstate.stable;
                                        _empreinte = newEmpreinte;
                                        return true;
                                    }
                                }
                            }
                        }

                    }
                    cmin = org;
                    dmin = delta;
                    // lastGoodDelta = (int)delta;
                    lastTotal = (int)total;
                    if (delta < conf.delta)
                    {
                        state = builderstate.stable;
                        _empreinte = newEmpreinte;
                        return true;
                    }

                    mustUpdate = true;

                    d = 0;

                    if (atomicMode > 0)
                    {
                        //if (atomicMode > conf.cloop)
                        //{
                        //    mustUpdate = false;
                        //    log("abandon recherche cartésienne cause cloop");
                        //    state = builderstate.stable;
                        //    _empreinte = newEmpreinte;
                        //    return true;
                        //}

                        //if (dminAlready && dmaxAlready)
                        //{
                        //    mustUpdate = false;
                        //    Program.log.log("abandon recherche cartésienne cause cycle");
                        //    return true;
                        //}

                        d = 0.5 * (cmax - cmin);
                    }
                    else
                    {
                        double _d = int.MaxValue;
                        if (cmax >= 0 && cmin >= 0)
                            _d = cmax - cmin;
                        foreach (Level level in conf.levels)
                        {
                            var aux = (delta - level.d) * level.inf;
                            if (aux > d)
                                d = aux;
                        }
                        if (d > _d)
                            d = _d;
                    }

                }
                else if (delta < 0)
                {

                    if (mem0.ajouts.Count > 0)
                        maxMem = new Memory(mem0);

                    mustUpdate = true;
                    canGo = false;

                    //if (maxMem != null && minMem != null)
                    //{
                    //    if (atomicMode < 0)
                    //        atomicMode = 0;
                    //    atomicMode++;
                    //}

                    cmax = org;
                    dmax = delta;

                    d = 0;

                    if (atomicMode > 0)
                    {

                        bool identique = false;
                        if (minMem != null && maxMem != null
                            && maxMem.ajouts.Count == minMem.suppressions.Count)
                        {
                            identique = true;

                            for (int i = 0; i < minMem.suppressions.Count; i++)
                            {
                                if (!minMem.suppressions[i].Equals(maxMem.ajouts[i]))
                                {
                                    identique = false;
                                    break;

                                }
                            }

                        }

                        if (cancelSameConf)// || (mem0.ajouts.Count == 1 && mem0.suppressions.Count == 0))
                        {
                            log("retour derniere bonne valeur cause idem");
                            d = cmin - cmax;
                        }
                        else if (cancelNb)
                        {
                            log("abandon recherche cartésienne cause cloop");
                            d = cmin - cmax;

                        }

                        else if (identique && (minMem.suppressions.Count == 1 || atomicMode > conf.verif))
                        {
                            log("retour derniere bonne valeur cause cycle");
                            d = cmin - cmax;
                        }
                        //else if (atomicMode > conf.cloop)
                        //{                      
                        //    log("abandon recherche cartésienne cause cloop");
                        //    d = cmin - cmax;
                        //}
                        else
                            d = 0.5 * (cmin - cmax);
                    }
                    else
                    {
                        double _d = -int.MaxValue;
                        if (cmax >= 0 && cmin >= 0)
                            _d = cmin - cmax;

                        foreach (Level level in conf.levels)
                        {
                            var aux = (delta - conf.delta + level.d) * level.sup;
                            if (aux < d)
                                d = aux;
                        }
                        if (d < _d)
                            d = _d;
                    }

                    //d = (delta - conf.delta) * conf.sup;
                    //if (org <= lastGoodLevel)
                    //{
                    //    lastGoodLevel = -1;
                    //    lastGoodDelta = -1;
                    //    lastTotal = -1;
                    //}
                }
                if (Math.Abs(d) < 0.00000001)
                {
                    mustUpdate = false;
                    // unCart();
                    state = builderstate.stable;
                    _empreinte = newEmpreinte;
                    return true;
                }

                //if (dejaVu)
                //    Program.log.log("déjà vu lastGoodLevel:{0} lastGoodDelta:{1}", lastGoodLevel, lastGoodDelta);

                log("correction {0}", d);

                newv = org + d;
                if (newv <= 0)
                    newv = 0;
                newv = Math.Round(newv, 8);

                log(" valeur {0} --> {1}", org, newv);

                att.Value = newv.ToString();

            }
            finally
            {
                if (state == builderstate.stable)
                {
                    mustUpdate = false;
                    _empreinte = newEmpreinte;
                    canGo = true;
                }

                string strDate = "";
                var attD = xml.Attributes["date"];
                if (attD != null)
                    strDate = attD.Value;

                bool exist = File.Exists(file);

                using (TextWriter tw = new StreamWriter(file, true, Encoding.UTF8))
                {
                    // "quand\tdiff\tcorr\torg\tnew\tcmin\tcmax\tdmin\tdmax\tatomic\tAdd\tiAdd\ttSupp\tiSup\tstate\t\r\n");
                    if (!exist)
                        tw.WriteLine("quand\ttarget\tdelta\tinf\tsup\ttotal\tdiff\tcorr\torg\tnew\tcmin\tcmax\tdmin\tdmax\tatomic\tstate");
                    tw.Write(DateTime.Now); tw.Write(SEP);
                    tw.Write(target.ToString("0.###")); tw.Write(SEP);
                    tw.Write(conf.delta.ToString("0.###")); tw.Write(SEP);
                    tw.Write(conf.levels[0].inf.ToString("0.###")); tw.Write(SEP);
                    tw.Write(conf.levels[0].sup.ToString("0.###")); tw.Write(SEP);
                    tw.Write(total.ToString("0.###")); tw.Write(SEP);
                    tw.Write((-delta).ToString("0.###")); tw.Write(SEP);
                    tw.Write(d.ToString("0.###")); tw.Write(SEP);
                    tw.Write(org.ToString("0.###")); tw.Write(SEP);
                    tw.Write(newv.ToString("0.###")); tw.Write(SEP);
                    tw.Write(cmin.ToString("0.###")); tw.Write(SEP);
                    tw.Write(cmax.ToString("0.###")); tw.Write(SEP);
                    tw.Write(dmin.ToString("0.###")); tw.Write(SEP);
                    tw.Write(dmax.ToString("0.###")); tw.Write(SEP);
                    tw.Write(atomicMode); tw.Write(SEP);
                    tw.Write(state); tw.Write(SEP);
                    tw.WriteLine();

                }

                if (canGo)
                    // makeDelta(mem1, "reel", @"..\rhisto.txt");
                    makeDelta(mem1, "reel", DAT + "rhisto." + conf.name + ".txt");
            }

            return canGo;

        }

        public static void Log()
        {
            if (main == null)
                return;
            main.log();
        }

        public override string ToString()
        {
            var strMin = minMem == null ? "" : minMem.suppressions.Count.ToString();
            var strMax = maxMem == null ? "" : maxMem.ajouts.Count.ToString();
            var strName = string.Format("{0} ({1})", name, resolveOrder());
            return string.Format("{0} {1} delta->{2} correction->{3}  mustUpdate->{4} canGo {5} atomic {6} idem {7} ({8}/{9})", strName, state, delta.ToString("0.###"), d.ToString("0.###"), mustUpdate, canGo, atomicMode, sameConfiguration, strMin, strMax);
        }
        private void log()
        {
            Program.log.log(sb.ToString());
            Program.log.log("total:{0}", total.ToString("0.###"));
            Program.log.log("delta {0}", delta.ToString("0.###"));
            Program.log.log("cmin {0}", cmin.ToString("0.###"));
            Program.log.log("dmin {0}", dmin.ToString("0.###"));
            Program.log.log("cmax {0}", cmax.ToString("0.###"));
            Program.log.log("dmax {0}", dmax.ToString("0.###"));
            if (MustUpdate)
            {
                foreach (AutoBuilder builder in list)
                {
                    if (builder.d != 0)
                    {
                        builder.log("correction {0}", builder.d);
                        builder.log(" valeur {0} --> {1}", builder.org, builder.newv);
                    }
                }
            }
            else Program.log.log("valeur non modifiée {0}", org);
            foreach (AutoBuilder builder in list)
            {
                builder.log(builder.ToString());
            }

        }

        private XmlElement xml;

        public void lookup(XmlElement node)
        {
            var att = node.Attributes["auto"];
            if (att != null)
            {
                // && conf.name.Equals(att.Value))
                var targets = att.Value.Split(';');
                foreach (var target in targets)
                {
                    if (name.Equals(target))
                    {
                        xml = node;
                        return;
                    }
                }

            }
            foreach (XmlNode sub in node.ChildNodes)
            {
                if (sub is XmlElement)
                {
                    lookup(sub as XmlElement);
                    if (xml != null)
                        return;
                }
            }
        }

        private int resolveOrder()
        {
            if (conf.order < int.MaxValue)
                return conf.order;
            return naturalOrder;
        }
        public int CompareTo(AutoBuilder other)
        {
            if (other == this)
                return 0;
            var cmp = resolveOrder().CompareTo(other.resolveOrder());
            if (cmp == 0)
                throw new ApplicationException(string.Format("deux builders d'ordre identique {0}/{1}", this.name, other.name));
            return cmp;
        }
    }
}
