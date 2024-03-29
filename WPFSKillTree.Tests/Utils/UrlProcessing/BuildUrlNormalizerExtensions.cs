﻿using System.Threading.Tasks;

namespace PoESkillTree.Utils.UrlProcessing
{
    internal static class BuildUrlNormalizerExtensions
    {
        public static Task<string> NormalizeAsync(this SkillTreeUrlNormalizer @this, string buildUrl)
            => @this.NormalizeAsync(buildUrl, (_, t) => t);
    }
}