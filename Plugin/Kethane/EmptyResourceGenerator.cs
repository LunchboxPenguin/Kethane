﻿using GeodesicGrid;
using System;

namespace Kethane
{
    internal class EmptyResourceGenerator : IResourceGenerator
    {
        private static readonly IBodyResources bodyResources = new BodyResources();

        public EmptyResourceGenerator() { }
        public EmptyResourceGenerator(ConfigNode node) { }

        public IBodyResources Load(CelestialBody body, ConfigNode node) { return bodyResources; }

        private class BodyResources : IBodyResources
        {
            public double MaxQuantity { get { return 0; } }
            public ConfigNode Save() { return new ConfigNode(); }
            public double? GetQuantity(Cell cell) { return null; }
            public double Extract(Cell cell, double amount) { throw new Exception("No deposit here"); }
        }
    }
}
